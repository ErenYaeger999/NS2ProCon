using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;
using HelixToolkit.WinUI.SharpDX;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NS2Pro.Core;
using System.Numerics;
using DV = System.Numerics.Vector3;
using Matrix = System.Numerics.Matrix4x4;
using Color4 = HelixToolkit.Maths.Color4;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;

namespace NS2Pro.App.Controls;

/// <summary>Original procedural 3D illustration, not Nintendo CAD or an imported third-party model.</summary>
public sealed class Controller3DView : UserControl, IDisposable
{
    private readonly DefaultEffectsManager effects = new();
    private readonly PerspectiveCamera camera = new();
    private readonly Viewport3DX viewport;
    private readonly Dictionary<string, Part> parts = new();
    private readonly PhongMaterial graphite = Material(.105f, .12f, .13f, 36), rubber = Material(.045f, .055f, .06f, 9),
        pearl = Material(.78f, .8f, .79f, 24), ink = Material(.7f, .75f, .75f, 10), active = Material(.035f, .55f, .48f, 56);
    private bool disposed;
    private ControllerInput? previousInput;
    private bool updated;
    private sealed record Part(List<MeshGeometryModel3D> Meshes, DV Position, bool Back = false);

    public Controller3DView()
    {
        Height = 320;
        viewport = new Viewport3DX
        {
            EffectsManager = effects,
            Camera = camera,
            BackgroundColor = Windows.UI.Color.FromArgb(255, 247, 249, 249),
            ShowViewCube = false,
            ShowCoordinateSystem = false,
            IsPanEnabled = false,
            IsInertiaEnabled = false,
            MSAA = MSAALevel.Four,
            FXAALevel = FXAALevel.High,
            EnableSSAO = true,
            SSAOIntensity = .7,
            ModelUpDirection = new DV(0, 1, 0),
            InputController = new OrbitInput()
        };
        viewport.Items.Add(new AmbientLight3D { Color = Windows.UI.Color.FromArgb(255, 112, 117, 124) });
        viewport.Items.Add(new DirectionalLight3D { Color = Windows.UI.Color.FromArgb(255, 225, 232, 235), Direction = new DV(-2, -3, -5) });
        viewport.Items.Add(new DirectionalLight3D { Color = Windows.UI.Color.FromArgb(255, 135, 151, 159), Direction = new DV(3, 1, -2) });
        viewport.Items.Add(new DirectionalLight3D { Color = Windows.UI.Color.FromArgb(255, 173, 181, 180), Direction = new DV(1, 2, 4) });
        viewport.RegisterPropertyChangedCallback(Viewport3DX.RenderExceptionProperty, (_, _) =>
        {
            if (viewport.RenderException is Exception error) Services.AppLog.Write("error", "三维渲染失败：" + error);
        });
        BuildModel(); ResetView();
        Loaded += (_, _) => ResetView();
        var root = new Grid(); root.Children.Add(viewport);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(10) };
        foreach (var (label, position) in new (string, DV)[] { ("正面", new(0, -.2f, 11)), ("背面", new(0, -.2f, -11)), ("复位", new(1.1f, -4.3f, 10.5f)) })
        {
            var button = new Button { Content = label, FontSize = 11, Padding = new(10, 5, 10, 5) };
            button.Click += (_, _) => SetCamera(position); controls.Children.Add(button);
        }
        root.Children.Add(controls); Content = root;
        AutomationProperties.SetName(this, "NS2 Pro 三维输入预览，可拖动旋转、滚轮缩放");
    }
    public void ResetView() => SetCamera(new(1.1f, -4.3f, 10.5f));
    private void SetCamera(DV p) { camera.Position = p; camera.LookDirection = new DV(0, -.25f, 0) - p; camera.UpDirection = new(0, 1, 0); camera.FieldOfView = 34; camera.NearPlaneDistance = .1; camera.FarPlaneDistance = 100; }

    private void BuildModel()
    {
        Vector2[] shell = [new(-2.2f, 1.3f), new(-2.7f, 1.08f), new(-2.98f, .45f), new(-3.4f, -1.65f), new(-3.25f, -2.05f), new(-2.87f, -2.12f), new(-2.35f, -1.65f), new(-1.55f, -.95f), new(0, -.88f), new(1.55f, -.95f), new(2.35f, -1.65f), new(2.87f, -2.12f), new(3.25f, -2.05f), new(3.4f, -1.65f), new(2.98f, .45f), new(2.7f, 1.08f), new(2.2f, 1.3f), new(0, 1.42f)];
        Add(Loft(shell, [(.77f, -.56f), (.94f, -.44f), (1f, -.1f), (.99f, .1f), (.91f, .32f), (.45f, .38f), (.02f, .38f)]), pearl);
        Vector2[] face = [new(-2.12f, 1.27f), new(-2.6f, 1.02f), new(-2.78f, .45f), new(-2.94f, -.65f), new(-2.65f, -1.17f), new(-1.6f, -.86f), new(0, -.83f), new(1.6f, -.86f), new(2.65f, -1.17f), new(2.94f, -.65f), new(2.78f, .45f), new(2.6f, 1.02f), new(2.12f, 1.27f), new(0, 1.38f)];
        Add(Loft(face, [(.96f, .08f), (1f, .24f), (.985f, .4f), (.93f, .48f), (.5f, .51f), (.02f, .51f)]), graphite);
        Add(Cylinder(new(0, 1.28f, -.08f), new(0, 1.47f, -.08f), .16f), rubber);
        Add(Cylinder(new(0, -1.02f, -.15f), new(0, -1.1f, -.15f), .09f), rubber);
        AddButton("SYNC", new(0, 1.43f, .05f), .075f, .08f);
        AddButton("L", new(-1.98f, 1.13f, .2f), .5f, .22f, 1.5f, .46f);
        AddButton("R", new(1.98f, 1.13f, .2f), .5f, .22f, 1.5f, .46f);
        AddButton("ZL", new(-1.95f, 1.06f, -.4f), .5f, .21f, 1.35f, .59f, true);
        AddButton("ZR", new(1.95f, 1.06f, -.4f), .5f, .21f, 1.35f, .59f, true);
        AddStick("LS", new(-1.82f, .42f, .48f)); AddStick("RS", new(1.05f, -.48f, .49f));
        foreach (var (name, x, y) in new (string, float, float)[] { ("X", 2.02f, .82f), ("Y", 1.57f, .38f), ("A", 2.47f, .38f), ("B", 2.02f, -.06f) }) AddButton(name, new(x, y, .48f), .205f, .14f);
        foreach (var (name, x, y) in new (string, float, float)[] { ("↑", -1f, -.18f), ("↓", -1f, -.8f), ("←", -1.31f, -.49f), ("→", -.69f, -.49f) }) AddButton(name, new(x, y, .49f), .16f, .12f, .33f, .33f);
        AddButton("−", new(-.64f, .79f, .5f), .135f, .08f); AddButton("+", new(.6f, .79f, .5f), .135f, .08f);
        AddButton("□", new(-.4f, -.16f, .51f), .14f, .07f, .27f, .27f);
        AddButton("HOME", new(.4f, .13f, .51f), .15f, .08f); AddButton("C", new(.4f, -.33f, .51f), .145f, .08f);
        AddButton("GL", new(-1.75f, -.59f, -.57f), .27f, .1f, .46f, .74f, true); AddButton("GR", new(1.75f, -.59f, -.57f), .27f, .1f, .46f, .74f, true);
    }
    private void AddStick(string name, DV p)
    {
        Add(Cylinder(new(p.X, p.Y, p.Z - .06f), new(p.X, p.Y, p.Z + .04f), .53f), rubber);
        Add(Cylinder(new(p.X, p.Y, p.Z + .025f), new(p.X, p.Y, p.Z + .055f), .46f), graphite);
        var meshes = new List<MeshGeometryModel3D> { Add(Cylinder(new(0, 0, 0), new(0, 0, .35f), .16f), graphite), Add(Cylinder(new(0, 0, .29f), new(0, 0, .42f), .37f), rubber) };
        var ring = new MeshBuilder();
        ring.AddTube(Enumerable.Range(0, 65).Select(i => new Vector3(.322f * MathF.Cos(i * MathF.Tau / 64), .322f * MathF.Sin(i * MathF.Tau / 64), .425f)).ToList(), .032f, 8, false, false, true);
        meshes.Add(Add(ring.ToMeshGeometry3D(), graphite)); parts[name] = new(meshes, p);
        foreach (var m in meshes) m.HxTransform3D = Matrix.CreateTranslation(p);
    }
    private void AddButton(string name, DV p, float radius, float depth, float width = 0, float height = 0, bool back = false)
    {
        MeshGeometry3D shape;
        if (width > 0)
        {
            Vector2[] rect = [new(-width / 2 + .08f, -height / 2), new(width / 2 - .08f, -height / 2), new(width / 2, -height / 2 + .08f), new(width / 2, height / 2 - .08f), new(width / 2 - .08f, height / 2), new(-width / 2 + .08f, height / 2), new(-width / 2, height / 2 - .08f), new(-width / 2, -height / 2 + .08f)];
            shape = Loft(rect, [(.92f, 0), (1f, .025f), (1f, depth - .025f), (.9f, depth), (.02f, depth)]);
        }
        else shape = Cylinder(new(0, 0, 0), new(0, 0, depth), radius);
        var meshes = new List<MeshGeometryModel3D> { Add(shape, rubber) };
        if (name != "SYNC") meshes.Add(Add(Letter(name, Math.Min(radius * .65f, .11f), depth + .005f), ink));
        var part = new Part(meshes, p, back); parts[name] = part; Transform(part, 0, 0, false);
    }
    private static MeshGeometry3D Letter(string text, float size, float z)
    {
        string key = text switch { "HOME" => "H", "GL" => "L", "GR" => "R", _ => text };
        float[][] strokes = key switch
        {
            "A" => [[-1, -1, 0, 1, 1, -1], [-.55f, -.2f, .55f, -.2f]],
            "B" => [[-1, -1, -1, 1, .2f, 1, .85f, .6f, .2f, 0, -1, 0], [.2f, 0, 1, -.5f, .4f, -1, -1, -1]],
            "X" => [[-1, 1, 1, -1], [-1, -1, 1, 1]],
            "Y" => [[-1, 1, 0, 0, 1, 1], [0, 0, 0, -1]],
            "+" => [[-1, 0, 1, 0], [0, -1, 0, 1]],
            "−" => [[-1, 0, 1, 0]],
            "C" => [[1, .75f, .35f, 1, -.6f, .7f, -1, 0, -.6f, -.7f, .35f, -1, 1, -.75f]],
            "L" => [[-.6f, 1, -.6f, -1, .7f, -1]],
            "R" => [[-.7f, -1, -.7f, 1, .3f, 1, .8f, .5f, .3f, 0, -.7f, 0], [0, 0, .9f, -1]],
            "ZL" or "ZR" => [[-1, 1, 1, 1, -1, -1, 1, -1]],
            "H" => [[-1, 0, 0, 1, 1, 0], [-.65f, .3f, -.65f, -1, .65f, -1, .65f, .3f]],
            "□" => [[-1, -1, -1, 1, 1, 1, 1, -1, -1, -1]],
            "↑" => [[-.6f, 0, 0, .7f, .6f, 0]],
            "↓" => [[-.6f, 0, 0, -.7f, .6f, 0]],
            "←" => [[0, -.6f, -.7f, 0, 0, .6f]],
            "→" => [[0, -.6f, .7f, 0, 0, .6f]],
            _ => []
        };
        var builder = new MeshBuilder();
        foreach (var s in strokes) for (int i = 0; i < s.Length - 2; i += 2) builder.AddCylinder(new(s[i] * size, s[i + 1] * size, z), new(s[i + 2] * size, s[i + 3] * size, z), .012f, 6);
        return builder.ToMeshGeometry3D();
    }
    private MeshGeometryModel3D Add(MeshGeometry3D geometry, PhongMaterial material)
    {
        var mesh = new MeshGeometryModel3D { Geometry = geometry, Material = material, CullMode = SharpDX.Direct3D11.CullMode.None, IsHitTestVisible = false }; viewport.Items.Add(mesh); return mesh;
    }
    private static PhongMaterial Material(float r, float g, float b, float shine) => new() { DiffuseColor = new Color4(r, g, b, 1), AmbientColor = new Color4(.17f, .17f, .17f, 1), SpecularColor = new Color4(.2f, .22f, .22f, 1), SpecularShininess = shine };
    private static MeshGeometry3D Cylinder(Vector3 a, Vector3 b, float radius) { var mesh = new MeshBuilder(); mesh.AddCylinder(a, b, radius, 48, true, true); return mesh.ToMeshGeometry3D(); }
    private static MeshGeometry3D Loft(Vector2[] outline, (float Scale, float Z)[] rings)
    {
        var points = new List<Vector2>();
        for (int i = 0; i < outline.Length; i++) for (int j = 0; j < 10; j++)
        {
            var p0 = outline[(i + outline.Length - 1) % outline.Length]; var p1 = outline[i]; var p2 = outline[(i + 1) % outline.Length]; var p3 = outline[(i + 2) % outline.Length]; float t = j / 10f;
            points.Add(.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
        }
        var mesh = new MeshBuilder(true, false); int n = points.Count;
        foreach (var ring in rings) foreach (var p in points) mesh.Positions.Add(new(p.X * ring.Scale, p.Y * ring.Scale, ring.Z));
        for (int r = 0; r < rings.Length; r++) for (int i = 0; i < n; i++)
        {
            var tangent = mesh.Positions[r * n + (i + 1) % n] - mesh.Positions[r * n + (i + n - 1) % n];
            var radial = mesh.Positions[Math.Min(r + 1, rings.Length - 1) * n + i] - mesh.Positions[Math.Max(r - 1, 0) * n + i];
            mesh.Normals!.Add(Vector3.Normalize(Vector3.Cross(tangent, radial)));
        }
        for (int r = 0; r < rings.Length - 1; r++) for (int i = 0; i < n; i++) { int a = r * n + i, b = r * n + (i + 1) % n, c = (r + 1) * n + i, d = (r + 1) * n + (i + 1) % n; foreach (int v in new[] { a, b, c, b, d, c }) mesh.TriangleIndices.Add(v); }
        // Cap vertices need independent normals; sharing edge normals produces diagonal shading artifacts.
        foreach (int r in new[] { 0, rings.Length - 1 })
        {
            int center = mesh.Positions.Count;
            mesh.Positions.Add(new(0, 0, rings[r].Z)); mesh.Normals!.Add(r == 0 ? -Vector3.UnitZ : Vector3.UnitZ);
            foreach (var p in points) { mesh.Positions.Add(new(p.X * rings[r].Scale, p.Y * rings[r].Scale, rings[r].Z)); mesh.Normals.Add(r == 0 ? -Vector3.UnitZ : Vector3.UnitZ); }
            for (int i = 0; i < n; i++) foreach (int v in r == 0 ? new[] { center, center + 1 + (i + 1) % n, center + 1 + i } : new[] { center, center + 1 + i, center + 1 + (i + 1) % n }) mesh.TriangleIndices.Add(v);
        }
        return mesh.ToMeshGeometry3D();
    }
    private static void Transform(Part part, float x, float y, bool pressed)
    {
        var matrix = Matrix.CreateRotationY(x * .32f) * Matrix.CreateRotationX(-y * .32f); if (part.Back) matrix *= Matrix.CreateRotationY(MathF.PI);
        matrix *= Matrix.CreateTranslation(part.Position + new DV(0, 0, pressed ? (part.Back ? .045f : -.045f) : 0)); foreach (var m in part.Meshes) m.HxTransform3D = matrix;
    }
    public void Update(ControllerInput? state)
    {
        if (disposed || (updated && ReferenceEquals(previousInput, state))) return;
        previousInput = state; updated = true;
        int r = state?.RightButtons ?? 0, l = state?.LeftButtons ?? 0, e = state?.ExtraButtons ?? 0;
        foreach (var (name, part) in parts)
        {
            bool pressed = name switch
            {
                "B" => (r & 1) != 0,
                "A" => (r & 2) != 0,
                "Y" => (r & 4) != 0,
                "X" => (r & 8) != 0,
                "R" => (r & 16) != 0,
                "ZR" => (r & 32) != 0,
                "+" => (r & 64) != 0,
                "RS" => (r & 128) != 0,
                "↓" => (l & 1) != 0,
                "→" => (l & 2) != 0,
                "←" => (l & 4) != 0,
                "↑" => (l & 8) != 0,
                "L" => (l & 16) != 0,
                "ZL" => (l & 32) != 0,
                "−" => (l & 64) != 0,
                "LS" => (l & 128) != 0,
                "HOME" => (e & 1) != 0,
                "□" => (e & 2) != 0,
                "GR" => (e & 4) != 0,
                "GL" => (e & 8) != 0,
                "C" => (e & 16) != 0,
                _ => false
            };
            part.Meshes[0].Material = pressed ? active : (name is "LS" or "RS" ? graphite : rubber);
            if (name is "LS" or "RS") { var stick = name == "LS" ? state?.UnfilteredLeft : state?.UnfilteredRight; Transform(part, (float)(stick?.X ?? 0), (float)(stick?.Y ?? 0), pressed); part.Meshes[1].Material = pressed ? active : rubber; }
            else Transform(part, 0, 0, pressed);
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; viewport.Dispose(); effects.Dispose(); }
    private sealed class OrbitInput : InputController
    {
        protected override bool IsStartRotate(Microsoft.UI.Input.PointerPointProperties properties) => properties.IsLeftButtonPressed || properties.IsRightButtonPressed;
    }
}
