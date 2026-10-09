using System.Text.Json;

namespace SenjiDownloader;

internal static class UiTests
{
    public static int Run(string report)
    {
        var results = new List<object>();
        var errors = new List<string>();
        var success = false;
        ThreadExceptionEventHandler capture = (_, e) => errors.Add(e.Exception.ToString());
        Application.ThreadException += capture;
        using var form = new MainForm(new Preferences { AutoUpdate = false }, null, background: true, preview: true);
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            try
            {
                foreach (var page in new[] { "downloads", "conversion", "settings" })
                {
                    form.ShowPage(page);
                    foreach (var combo in Descendants(form).OfType<DarkCombo>().Where(c => c.Visible).ToArray())
                    {
                        var popup = combo.OptionsMenu;
                        popup.Opacity = 0;
                        var changes = 0;
                        combo.SelectedIndexChanged += (_, _) => changes++;
                        var last = 0;
                        for (var cycle = 0; cycle < 5; cycle++)
                        {
                            for (var i = 0; i < popup.Items.Count; i++)
                            {
                                combo.Enabled = true;
                                combo.AccessibilityObject.DoDefaultAction();
                                if (!popup.Visible) throw new Exception("El selector no se abrió.");
                                popup.Items[i].PerformClick();
                                Application.DoEvents();
                                if (popup.IsDisposed || popup.Visible || combo.SelectedIndex != i)
                                    throw new Exception("No se cerró o seleccionó correctamente: " + combo.AccessibleName);
                                last = i;
                            }
                            combo.AccessibilityObject.DoDefaultAction();
                            popup.Close(ToolStripDropDownCloseReason.Keyboard);
                            Application.DoEvents();
                            if (popup.IsDisposed || combo.SelectedIndex != last) throw new Exception("Cerrar sin elegir cambió la selección.");
                        }
                        if (changes == 0) throw new Exception("El selector no notificó sus cambios.");
                        results.Add(new { page, selector = combo.AccessibleName ?? "Sesión del navegador", selections = 5 * popup.Items.Count, changes });
                    }
                }
                foreach (var page in new[] { "history", "downloads", "conversion", "settings", "downloads" })
                {
                    form.ShowPage(page); form.ClientSize = new Size(960, 690); Application.DoEvents();
                    form.ClientSize = new Size(1120, 748); Application.DoEvents();
                }
                if (errors.Count != 0) throw new Exception(string.Join("\n", errors));
                var menus = Descendants(form).OfType<DarkCombo>().Select(c => c.OptionsMenu).ToArray();
                form.Quit(); form.Dispose();
                if (menus.Any(m => !m.IsDisposed)) throw new Exception("Los menús no se liberaron con la ventana.");
                success = true;
            }
            catch (Exception ex) { errors.Add(ex.ToString()); }
            finally
            {
                File.WriteAllText(report, JsonSerializer.Serialize(new { passed = success, version = UpdateService.CurrentVersion, results, errors }, Storage.Json));
                if (!form.IsDisposed) form.Quit();
                Application.ExitThread();
            }
        });
        try { Application.Run(form); }
        finally { Application.ThreadException -= capture; }
        return success ? 0 : 1;
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
