using System.Reflection;
using System.Text.Json;

internal static class ComboPaintChecks
{
    internal static void Run(Assembly assembly, string evidence)
    {
        using var host = new Form { ClientSize = new Size(720, 130), ShowInTaskbar = false };
        using var combo = (ComboBox)Activator.CreateInstance(assembly.GetType("InsightScreenRecorder.StyledComboBox")!, true)!;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Bounds = new Rectangle(20, 20, 18, 30);
        combo.Items.AddRange(new object[] { "PNG", "JPEG" });
        combo.SelectedIndex = 0;
        host.Controls.Add(combo);
        host.Show();
        Application.DoEvents();
        var regions = new List<Rectangle>();
        combo.Invalidated += (_, e) => regions.Add(e.InvalidRect);
        combo.Width = 620;
        Application.DoEvents();
        File.WriteAllText(Path.Combine(evidence, "combo-resize-invalidation.json"), JsonSerializer.Serialize(new { combo.ClientSize, Regions = regions }, new JsonSerializerOptions { IncludeFields = true }));
        if (!regions.Any(r => r.Contains(combo.ClientRectangle)))
            throw new Exception("Resized settings field must invalidate its complete new client area without mouse hover.");
        if (combo.Text != "PNG" || combo.SelectedIndex != 0)
            throw new Exception("Field resize must preserve its selected value.");
        Console.WriteLine("PASS resized field invalidates full client area without hover and preserves selection");
        regions.Clear();
        combo.Width = 310;
        Application.DoEvents();
        if (!regions.Any(r => r.Contains(combo.ClientRectangle)))
            throw new Exception("Shrinking the field must also repaint its complete client area.");
        Console.WriteLine("PASS shrinking field repaints full client area");
    }
}
