#if TOOLS
using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// Adds a dock with the "Export region" and "Apply stamp" buttons and a list of problems, each a button that goes to its tile. Under
/// <c>--editor</c>, the <c>--press-export=&lt;scene&gt;</c> and <c>--press-apply=&lt;scene&gt;</c> commands open the scene and press
/// the real buttons, so they can be tested with no one there.
/// </summary>
[Tool]
public partial class RegionToolsPlugin : EditorPlugin
{
    private EditorDock _dock = null!;
    private Button _export = null!;
    private Button _apply = null!;
    private Label _status = null!;
    private VBoxContainer _problems = null!;

    private static Region? EditedRegion => EditorInterface.Singleton.GetEditedSceneRoot() as Region;

    /// <inheritdoc />
    public override void _EnterTree()
    {
        _export = new Button { Text = "Export region", TooltipText = "Save the scene, check it, and write its region file if nothing's wrong." };
        _export.Pressed += ExportEditedScene;
        _apply = new Button { Text = "Apply stamp", TooltipText = "Write the selected StampMarker's battle map to the layers, at the marker. Undo takes it back." };
        _apply.Pressed += ApplySelectedStamp;
        _status = new Label { Text = "Open a region scene, then export it.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _problems = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_problems);
        var column = new VBoxContainer();
        column.AddChild(_export);
        column.AddChild(_apply);
        column.AddChild(_status);
        column.AddChild(scroll);
        _dock = new EditorDock { Title = "Region", DefaultSlot = EditorDock.DockSlot.RightBl };
        _dock.AddChild(column);
        AddDock(_dock);

        if (Commands.Arg("press-export") is { } export)
            PressWhenReady(export, _export);
        else if (Commands.Arg("press-apply") is { } apply)
            PressWhenReady(apply, _apply);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock.QueueFree();
    }

    private void ExportEditedScene()
    {
        Begin("Exporting...");
        try
        {
            if (EditedRegion is not { } root)
            {
                Finish("The open scene isn't a region.", ["Open a scene whose root is a Region node, or make one with --new."]);
                return;
            }

            if (root.SceneFilePath.Length == 0)
            {
                Finish("Not exported.", ["Save the scene first: use Scene > Save Scene As, then export again."]);
                return;
            }

            // The scene is the source of truth, so what's exported has to be what's saved. SaveScene returns Ok even when the write
            // fails, as it does on a read-only file, so the file's own time is what proves it.
            var scene = ProjectSettings.GlobalizePath(root.SceneFilePath);
            var before = File.GetLastWriteTimeUtc(scene);
            var saved = EditorInterface.Singleton.SaveScene();
            if (saved != Error.Ok || File.GetLastWriteTimeUtc(scene) <= before)
            {
                Finish("Not exported.", ["Save the scene first: it couldn't be saved. Use Scene > Save Scene As, or check the file isn't read-only, then export again."]);
                return;
            }

            var result = RegionScene.Export(root, Commands.Arg("rules"));
            Finish(result.Status, result.Problems);
        }
        catch (Exception ex)
        {
            Finish("The export failed, and no file was written.", [$"{ex.GetType().Name}: {ex.Message} Export again, and report this if it stays."]);
        }
    }

    private void ApplySelectedStamp()
    {
        Begin("Stamping...");
        try
        {
            if (EditedRegion is not { } root)
            {
                Finish("The open scene isn't a region.", ["Open a scene whose root is a Region node."]);
                return;
            }
            if (EditorInterface.Singleton.GetSelection().GetSelectedNodes().OfType<StampMarker>().FirstOrDefault() is not { } marker)
            {
                Finish("No stamp selected.", ["Select a StampMarker under Stamps, then press Apply stamp."]);
                return;
            }
            if (StampMaps.Read(Commands.Arg("rules") ?? RegionPaths.Rules, marker.Map) is not { } map)
            {
                Finish("Not stamped.", [$"Unknown map '{marker.Map}'. Set the marker's Map to a file in rules/maps."]);
                return;
            }

            var stroke = StampBrush.Apply(map, marker.Tile, marker.HeightOffset, marker.AppliedTile, root.Size.X, root.Size.Y);
            if (stroke.Problems.Length > 0)
            {
                Finish("Not stamped.", stroke.Problems);
                return;
            }
            var material = root.GetNode<TileMapLayer>(RegionScene.MaterialLayer);
            var sources = RegionScene.MaterialSources(material.TileSet);
            var noSwatch = stroke.Cells.Select(c => c.Material).Distinct(StringComparer.Ordinal).Where(m => !sources.ContainsKey(m)).ToList();
            if (noSwatch.Count > 0)
            {
                Finish("Not stamped.", [$"The palette has no swatch for {string.Join(", ", noSwatch)}. Rebuild the palette: see the README."]);
                return;
            }

            Write(root, marker, stroke, sources);
            Finish(stroke.Report, []);
        }
        catch (Exception ex)
        {
            Finish("The stamp failed.", [$"{ex.GetType().Name}: {ex.Message} Undo, and report this if it stays."]);
        }
    }

    /// <summary>Writes the stroke as one undoable action.</summary>
    private void Write(Region root, StampMarker marker, StampStroke stroke, Dictionary<string, int> sources)
    {
        var undo = GetUndoRedo();
        var material = root.GetNode<TileMapLayer>(RegionScene.MaterialLayer);
        var height = root.GetNode<TileMapLayer>(RegionScene.HeightLayer);
        var flags = root.GetNode<TileMapLayer>(RegionScene.FlagsLayer);

        void Put(TileMapLayer layer, Vector2I cell, int source, Vector2I atlas)
        {
            undo.AddDoMethod(layer, TileMapLayer.MethodName.SetCell, cell, source, atlas, 0);
            undo.AddUndoMethod(layer, TileMapLayer.MethodName.SetCell, cell, layer.GetCellSourceId(cell), layer.GetCellAtlasCoords(cell), layer.GetCellAlternativeTile(cell));
        }

        undo.CreateAction($"Apply stamp {stroke.Map}", UndoRedo.MergeMode.Disable, root);
        foreach (var cell in stroke.Cells)
        {
            var at = new Vector2I(cell.At.X, cell.At.Y);
            Put(material, at, sources[cell.Material], Vector2I.Zero);
            var heightTile = RegionScene.HeightAtlas(cell.Height);
            Put(height, at, heightTile is null ? -1 : 0, heightTile ?? new Vector2I(-1, -1));
            var flagsTile = RegionScene.FlagsAtlas(cell.Flags);
            Put(flags, at, flagsTile is null ? -1 : 0, flagsTile ?? new Vector2I(-1, -1));
        }
        undo.AddDoProperty(marker, StampMarker.PropertyName.AppliedAt, new Vector2I(stroke.At.X, stroke.At.Y));
        undo.AddUndoProperty(marker, StampMarker.PropertyName.AppliedAt, marker.AppliedAt);
        undo.CommitAction();
    }

    private void Begin(string status)
    {
        _status.Text = status;
        foreach (var child in _problems.GetChildren())
        {
            _problems.RemoveChild(child);
            child.QueueFree();
        }
        if (EditedRegion is { } root)
            root.Highlight = null;
    }

    private void Finish(string status, IReadOnlyList<string> problems)
    {
        _status.Text = status;
        foreach (var problem in problems)
        {
            var button = new Button
            {
                Text = problem,
                TooltipText = problem,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Flat = RegionExport.TileIn(problem) is null,
            };
            if (RegionExport.TileIn(problem) is { } tile)
                button.Pressed += () => GoTo(tile);
            _problems.AddChild(button);
        }
    }

    /// <summary>Centres the 2D view on a tile and marks it.</summary>
    private void GoTo(Tile tile)
    {
        if (EditedRegion is not { } root)
            return;
        root.Highlight = new Vector2I(tile.X, tile.Y);
        EditorInterface.Singleton.SetMainScreenEditor("2D");
        var centre = root.ToGlobal((new Vector2(tile.X, tile.Y) + (Vector2.One / 2)) * PaintedRegion.TilePixels);
        EditorInterface.Singleton.GetBaseControl().FindChildren("*", "CanvasItemEditor", recursive: true, owned: false).FirstOrDefault()?.Call("center_at", centre);
    }

    /// <summary>The test hook: opens the scene, presses the button as a click does, prints what the dock shows, and quits.</summary>
    private async void PressWhenReady(string scene, Button button)
    {
        var files = EditorInterface.Singleton.GetResourceFilesystem();
        do
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        while (files.IsScanning());

        EditorInterface.Singleton.OpenSceneFromPath(RegionPaths.Scene(scene));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (button == _apply)
        {
            var marker = SelectStamp();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"SNAPPED: {marker.Position}");
        }
        button.EmitSignal(BaseButton.SignalName.Pressed);
        Print();

        if (button == _apply && EditedRegion is { } root)
            ProveUndo(root);
        if (OS.GetCmdlineUserArgs().Contains("--click-first-problem") && _problems.GetChildCount() > 0)
            await ClickFirstProblem();
        GetTree().Quit(_problems.GetChildCount() == 0 ? 0 : 1);
    }

    private void Print()
    {
        GD.Print($"STATUS: {_status.Text}");
        foreach (var problem in _problems.GetChildren().OfType<Button>())
            GD.Print($"PROBLEM: {problem.Text}");
    }

    /// <summary>Selects the first stamp, dropping it part way into the tile <c>--move=x,y</c> names, as a drag does.</summary>
    private static StampMarker SelectStamp()
    {
        var marker = EditedRegion!.GetNode(RegionScene.Stamps).GetChild<StampMarker>(0);
        if (Commands.Arg("move") is { } move && move.Split(',') is [var x, var y])
            marker.Position = marker.PlaceOn(new Tile(int.Parse(x), int.Parse(y))) + new Vector2(7, 9);
        EditorInterface.Singleton.GetSelection().AddNode(marker);
        return marker;
    }

    private void ProveUndo(Region root)
    {
        GD.Print($"APPLIED: {Fingerprint(root)}");
        var undo = GetUndoRedo();
        undo.GetHistoryUndoRedo(undo.GetObjectHistoryId(root)).Undo();
        GD.Print($"UNDONE: {Fingerprint(root)}");
    }

    private static string Fingerprint(Region root)
    {
        var read = RegionScene.Read(root);
        var marker = root.GetNode(RegionScene.Stamps).GetChild<StampMarker>(0);
        return $"materials {read.Materials.Count} (grass {read.Materials.Values.Count(m => m == "grass/lush")}), heights {read.Heights.Count}, flags {read.Flags.Count}, appliedAt {marker.AppliedAt}";
    }

    private async Task ClickFirstProblem()
    {
        var first = _problems.GetChild<Button>(0);
        var before = EditorInterface.Singleton.GetEditorViewport2D().GlobalCanvasTransform.Origin;
        first.EmitSignal(BaseButton.SignalName.Pressed);
        first.Size = new Vector2(220, 0);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var editor = EditorInterface.Singleton.GetBaseControl().FindChildren("*", "CanvasItemEditor", recursive: true, owned: false).FirstOrDefault();
        var view = EditorInterface.Singleton.GetEditorViewport2D().GlobalCanvasTransform;
        GD.Print($"CLICKED: highlight {EditedRegion?.Highlight}, editor found {editor is not null}, center_at bound {editor?.HasMethod("center_at")}, view origin {before} to {view.Origin}, at 220 wide the button is {first.GetCombinedMinimumSize().Y} high");
    }
}
#endif
