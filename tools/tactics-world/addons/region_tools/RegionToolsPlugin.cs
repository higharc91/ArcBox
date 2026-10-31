#if TOOLS
using Godot;

namespace Tactics.World.Editor;

/// <summary>
/// Adds a dock with the "Export region" button and a list of what stopped the export. Run under <c>--editor</c>, the
/// <c>--press-export=&lt;scene&gt;</c> command opens the scene and presses the button, so the button can be tested with no one there.
/// </summary>
[Tool]
public partial class RegionToolsPlugin : EditorPlugin
{
    private EditorDock _dock = null!;
    private Button _export = null!;
    private Label _status = null!;
    private ItemList _problems = null!;

    /// <inheritdoc />
    public override void _EnterTree()
    {
        _export = new Button { Text = "Export region" };
        _export.Pressed += ExportEditedScene;
        _status = new Label { Text = "Open a region scene, then export it.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _problems = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
        _dock = new EditorDock { Title = "Region", DefaultSlot = EditorDock.DockSlot.RightBl };
        var column = new VBoxContainer();
        column.AddChild(_export);
        column.AddChild(_status);
        column.AddChild(_problems);
        _dock.AddChild(column);
        AddDock(_dock);

        if (Commands.Arg("press-export") is { } scene)
            PressExport(scene);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        RemoveDock(_dock);
        _dock.QueueFree();
    }

    private void ExportEditedScene()
    {
        _problems.Clear();
        if (EditorInterface.Singleton.GetEditedSceneRoot() is not Region root)
        {
            _status.Text = "The open scene isn't a region.";
            _problems.AddItem("Open a scene whose root is a Region node.");
            return;
        }

        // The scene is the source of truth, so what's exported is what's saved.
        EditorInterface.Singleton.SaveScene();
        var result = RegionScene.Export(root, Commands.Arg("rules"));
        foreach (var problem in result.Problems)
            _problems.AddItem(problem);
        _status.Text = result.Written is { } written
            ? $"Wrote {written}"
            : $"Not exported: {result.Problems.Length} problem{(result.Problems.Length == 1 ? "" : "s")}.";
    }

    private async void PressExport(string scene)
    {
        var files = EditorInterface.Singleton.GetResourceFilesystem();
        do
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        while (files.IsScanning());

        EditorInterface.Singleton.OpenSceneFromPath(RegionPaths.Scene(scene));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _export.EmitSignal(BaseButton.SignalName.Pressed);

        GD.Print($"STATUS: {_status.Text}");
        for (var i = 0; i < _problems.ItemCount; i++)
            GD.Print($"PROBLEM: {_problems.GetItemText(i)}");
        GetTree().Quit(_problems.ItemCount == 0 ? 0 : 1);
    }
}
#endif
