using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using SWGrade.Desktop;
using SWGrade.Domain;
using SWGrade.SolidWorks;

internal static class Program
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static int Main()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new App();
        app.InitializeComponent();
        int exitCode = 1;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await CheckSurfaceColorsAsync();
                exitCode = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }));
        Dispatcher.Run();
        return exitCode;
    }

    private static async Task CheckSurfaceColorsAsync()
    {
        var vertices = new List<float>();
        var values = new List<float>();
        AddStrip(-3, -1, -1, -1, vertices, values);
        AddStrip(-1, 1, 0, 0, vertices, values);
        AddStrip(1, 3, 1, 1, vertices, values);
        AddStrip(3, 5, -.8f, .8f, vertices, values);

        var surface = Construct<DeviationSurface>(vertices.ToArray(), values.ToArray());
        var deviation = Construct<SurfaceDeviation>(surface, surface);
        var scene = Construct<AnalysisScene>(null, null, SheetMetalMode.Automatic, GeometryCoordinates.Original);
        scene = (AnalysisScene)typeof(AnalysisScene).GetMethod("WithDeviation", Instance).Invoke(scene,
            new object[] { deviation, null });
        var view = new PartViewport { IsDeviationView = true, Scene = scene };
        var size = new Size(720, 550);
        view.Measure(size);
        view.Arrange(new Rect(size));
        await WaitForMeshAsync(view);
        ((ComboBox)view.FindName("StandardViews")).SelectedIndex = 1;

        var captures = new[] { 96, 120, 144 };
        foreach (int dpi in captures)
        {
            var colors = CaptureColors(view, dpi);
            Require(colors.Red > 100 && colors.Green > 100 && colors.Blue > 100 && colors.Intermediate > 40,
                $"Missing surface palette colors at {dpi} DPI.");
            Console.WriteLine($"PASS actual cloud colors and interpolation at {dpi} DPI");
        }

        var reference = (Button)view.FindName("ReferenceButton");
        Require(reference.Visibility == Visibility.Visible && reference.IsEnabled, "Reference side cannot be selected.");
        reference.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((TextBox)view.FindName("DeviationTolerance")).Text = "1";
        ((TextBox)view.FindName("DeviationRange")).Text = "2";
        typeof(PartViewport).GetMethod("ApplyDeviationScale", Instance)
            .Invoke(view, new object[] { view, new RoutedEventArgs() });
        await WaitForMeshAsync(view);
        var within = CaptureColors(view, 96);
        Require(within.Green > 1000 && within.Red == 0 && within.Blue == 0, "Tolerance did not recolor the surface.");
        Console.WriteLine("PASS reference selection and tolerance recoloring");
    }

    private static (int Red, int Green, int Blue, int Intermediate) CaptureColors(PartViewport view, int dpi)
    {
        view.UpdateLayout();
        var surface = (Border)view.FindName("Surface");
        var size = surface.RenderSize;
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            var bounds = new Rect(size);
            draw.DrawRectangle(new VisualBrush(surface) { Viewbox = bounds, ViewboxUnits = BrushMappingMode.Absolute },
                null, bounds);
        }
        int width = (int)Math.Ceiling(size.Width * dpi / 96);
        int height = (int)Math.Ceiling(size.Height * dpi / 96);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bytes = new byte[width * height * 4];
        bitmap.CopyPixels(bytes, width * 4, 0);
        int red = 0, green = 0, blue = 0, intermediate = 0;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            int b = bytes[i], g = bytes[i + 1], r = bytes[i + 2];
            if (r > g + 100 && r > b + 100) red++;
            if (g > r + 90 && g > b + 45) green++;
            if (b > r + 100 && b > g + 75) blue++;
            if (r >= 85 && r <= 140 && g >= 92 && g <= 130 && b >= 53 && b <= 75) intermediate++;
        }
        return (red, green, blue, intermediate);
    }

    private static void AddStrip(float left, float right, float leftValue, float rightValue,
        List<float> vertices, List<float> values)
    {
        vertices.AddRange(new[] { left, -1f, 0f, right, -1f, 0f, left, 1f, 0f,
            right, -1f, 0f, right, 1f, 0f, left, 1f, 0f });
        values.AddRange(new[] { leftValue, rightValue, leftValue, rightValue, rightValue, leftValue });
    }

    private static async Task WaitForMeshAsync(PartViewport view)
    {
        var visual = (ModelVisual3D)view.FindName("PartVisual");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (visual.Content == null && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        Require(visual.Content != null, "Cloud mesh did not become available.");
    }

    private static T Construct<T>(params object[] values) =>
        (T)Activator.CreateInstance(typeof(T), Instance, null, values, null);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
