using ArcBox.Host.Shell;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace ArcBox.Host.Games.Tactics;

/// <summary>Procedural journal, seals and waypoints; no generated UI art is required.</summary>
public partial class QuestHud(string root, HudArt art) : Control
{
    private static readonly Color Ink = new("382b24"), Paper = new("efdfbd"), Gold = new("e9bb69"), Muted = new("806b50");
    private ExploreView? _view;
    private QuestLogView? _journal;
    private BoardTerrain? _terrain;
    private BoardFit? _fit;
    private bool _walking;
    private float _open;
    private double _clock;
    private readonly Dictionary<string, Texture2D?> _pictures = [];

    public override void _Ready() { SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); MouseFilter = MouseFilterEnum.Ignore; }
    public void Frame(ExploreView? view, bool walking, BoardTerrain terrain, BoardFit fit, double delta)
    {
        (_view, _walking, _terrain, _fit) = (view, walking, terrain, fit);
        if (view?.QuestLog is { } journal) _journal = journal;
        _clock += delta;
        _open = Mathf.MoveToward(_open, walking && view?.QuestLog is not null ? 1 : 0, (float)delta / .28f);
        Visible = walking || _open > 0;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_view is null) return;
        DrawSetTransform(Vector2.Zero, 0, new Vector2(Size.X / 1920, Size.Y / 1080));
        if (_walking)
        {
            if (_view.Pinned is { } tracker)
            {
                Box(new Rect2(36, 152, 570, 116), new Color("292622"), Gold);
                DrawColoredPolygon([new(58,167),new(72,167),new(72,196),new(65,189),new(58,196)],Gold);
                Text(tracker.Name, new Vector2(88, 190), 28, Gold, 495, true);
                Text(tracker.Objective, new Vector2(60, 232), 26, Paper, 520);
                if (tracker.Target is { } target) Waypoint(target);
            }
            if (_view.QuestMarkers is { } markers)
                foreach (var marker in markers)
                    if (Point(marker.At) is { } p && p.X > 30 && p.X < 1890 && p.Y > 100 && p.Y < 1030)
                    {
                        var y = p.Y - 74 + (float)Math.Sin(_clock * 2.5) * 4;
                        DrawCircle(new Vector2(p.X, y - 10), 22, new Color("30251fdd"));
                        Text(marker.TurnIn ? "?" : "!", new Vector2(p.X - 10, y), 35, Gold, 50, true);
                    }
            if (_view.QuestEvent is { } ev && ev.Until > DateTimeOffset.UtcNow)
            {
                var label = ev.Kind switch { "start" => "Quest started", "update" => "Quest updated", "complete" => "Quest complete", _ => "Quest failed" };
                Box(new Rect2(620, 150, 700, 120), new Color("25252bf5"), Gold);
                Text(label.ToUpperInvariant(), new Vector2(650, 185), 24, Gold, 640, true);
                Text(ev.Name, new Vector2(650, 222), 32, Paper, 640, true);
                if (ev.Kind == "complete") Text($"+{ev.Gold} gold · +{ev.Xp} XP per member", new Vector2(650, 253), 24, Paper, 640);
            }
        }
        if (_open <= 0 || _journal is not { } journal) return;
        DrawRect(new Rect2(0,0,1920,1080), new Color(0,0,0,.6f * _open));
        var shift = (1 - _open) * 45;
        DrawSetTransform(new Vector2(0, shift), 0, new Vector2(Size.X / 1920, Size.Y / 1080));
        Box(new Rect2(175, 104, 1570, 870), new Color("4b3027"), Gold);
        Box(new Rect2(199, 126, 745, 826), Paper, Muted);
        Box(new Rect2(948, 126, 773, 826), new Color("f5e7c9"), Muted);
        DrawLine(new Vector2(946,133), new Vector2(946,940), new Color("9b7958"), 5);
        Text("WAYFARER'S JOURNAL", new Vector2(236, 190), 36, Ink, 660, true);
        Text("A record of promises kept", new Vector2(236, 226), 26, Muted, 660);
        float row = 280;
        foreach (var status in new[] { "active", "done", "failed" })
        {
            var entries = journal.Quests.Where(q => q.Status == status).ToArray();
            if (entries.Length == 0) continue;
            Text(status.ToUpperInvariant(), new Vector2(240,row), 25, Muted, 640, true); row += 28;
            foreach (var q in entries)
            {
                var seal = status == "active" ? new Color("a43c2b") : status == "done" ? new Color("a17c32") : new Color("736d65");
                if (q.Id == journal.Selected) Box(new Rect2(224,row,690,104), new Color("fff4d9"), Muted);
                DrawCircle(new Vector2(255,row+36), 17, seal);
                Text(status == "done" ? "✓" : "✦", new Vector2(244,row+44), 24, Paper, 40);
                Text(q.Name, new Vector2(289,row+36), 29, Ink, 590, true);
                Text(q.Objective, new Vector2(289,row+73), 25, Muted, 590);
                if (q.Id == journal.Pinned) DrawColoredPolygon([new(880,row),new(898,row),new(898,row+57),new(889,row+47),new(880,row+57)],new Color("a43c2b"));
                row += 116;
            }
        }
        var selected = journal.Quests.FirstOrDefault(q => q.Id == journal.Selected);
        if (selected is null) { Text("Speak to the people of Ashen Ford.\nA promise is where every story begins.",new Vector2(1000,320),32,Ink,650); return; }
        if (art.Speaker(selected.Portrait,"idle",null) is { } face && Picture(face.Path) is { } picture)
            DrawTextureRectRegion(picture,new Rect2(1000,160,112,112),new Rect2(face.Source.X,face.Source.Y,face.Source.Width,face.Source.Height));
        Text(selected.Giver,new Vector2(1138,195),26,Muted,540);
        Text(selected.Name,new Vector2(1138,237),32,Ink,540,true);
        DrawLine(new Vector2(996,300),new Vector2(1670,300),Muted,1);
        float at = 349;
        foreach (var history in selected.History)
        {
            if (history.Objective is { } objective && !history.Done) { at += Text(objective,new Vector2(1000,at),30,Ink,650,true); at += 8; }
            at += Text((history.Done ? "✓  " : "") + history.Log,new Vector2(1000,at),28,history.Done ? Muted : Ink,650) + 25;
        }
        if (selected.Result is { } result) at += Text(result,new Vector2(1000,at),28,Ink,650) + 24;
        if (selected.Rewards is { } rewards)
        {
            Text("REWARDS RECEIVED",new Vector2(1000,Math.Min(at,840)),24,Muted,650,true);
            Text($"{rewards.Gold} gold · {rewards.Xp} XP per member" + string.Concat(rewards.Items.Select(i => $" · {i.Count} × {i.Name}")),
                new Vector2(1000,Math.Min(at+36,882)),26,Ink,650);
        }
    }
    private Vector2? Point(Tile tile)
    {
        if (_terrain is null || _fit is null || !_terrain.Map.InBounds(tile)) return null;
        var point = _fit.ToScreen(BoardProjection.Corner(tile.X+.5f,tile.Y+.5f,_terrain.Top(tile)));
        return new Vector2(point.X,point.Y);
    }
    private void Waypoint(Tile tile)
    {
        if (Point(tile) is not { } p) return;
        p.Y -= 50;
        if (p.X > 64 && p.X < 1856 && p.Y > 280 && p.Y < 990)
        {
            var size = 10+(float)Math.Sin(_clock*3)*2;
            DrawCircle(p,18,new Color("30251fbb"));
            DrawPolyline([p+new Vector2(0,-size),p+new Vector2(size,0),p+new Vector2(0,size),p+new Vector2(-size,0),p+new Vector2(0,-size)],Gold,3,true);
        }
        else
        {
            var centre=new Vector2(960,570);var dir=(p-centre).Normalized();
            var t=Math.Min(850/Math.Max(Math.Abs(dir.X),.001f),420/Math.Max(Math.Abs(dir.Y),.001f));
            var tip=centre+dir*t;var side=new Vector2(-dir.Y,dir.X);
            DrawCircle(tip,25,new Color("30251fdd"));
            DrawColoredPolygon([tip+dir*18,tip-dir*12+side*13,tip-dir*12-side*13],Gold);
        }
    }
    private void Box(Rect2 rect,Color fill,Color border) { DrawRect(rect,fill);DrawRect(rect,border,false,2); }
    private float Text(string text,Vector2 at,int size,Color color,float width,bool bold=false)
    {
        var font=bold?ArcTheme.Bold:ArcTheme.Regular;var lines=new List<string>();var current="";
        foreach(var word in text.Replace("\n"," \n ").Split(' '))
        {
            if(word=="\n") {lines.Add(current);current="";continue;}
            var next=current.Length==0?word:current+" "+word;
            if(current.Length>0&&font.GetStringSize(next,fontSize:size).X>width) {lines.Add(current);current=word;} else current=next;
        }
        lines.Add(current);
        foreach(var line in lines) {DrawString(font,at,line,HorizontalAlignment.Left,width,size,color);at.Y+=size*1.22f;}
        return lines.Count*size*1.22f;
    }
    private Texture2D? Picture(string file)
    {
        if(_pictures.TryGetValue(file,out var found))return found;
        var path=System.IO.Path.Combine(root,file);var image=System.IO.File.Exists(path)?Image.LoadFromFile(path):null;
        return _pictures[file]=image is null||image.IsEmpty()?null:ImageTexture.CreateFromImage(image);
    }
}
