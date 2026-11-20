using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CarDisc;

public partial class Form1
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var layout = Controls
            .OfType<TableLayoutPanel>()
            .FirstOrDefault();

        if (layout?.GetControlFromPosition(0, 1)
            is FlowLayoutPanel buttons)
        {
            buttons.Controls.Add(
                MakeButton("Print track list", OpenTrackList));
        }
    }

    private void OpenTrackList()
    {
        if (readingFiles)
            return;

        var tracks = playlist.Items
            .Cast<MusicFile>()
            .ToArray();

        if (tracks.Length == 0)
        {
            MessageBox.Show(
                this, "Add some songs first.", "Print track list");
            return;
        }

        var text = new StringBuilder();

        for (int i = 0; i < tracks.Length; i++)
        {
            string name = System.IO.Path
                .GetFileNameWithoutExtension(tracks[i].FullPath);

            text.AppendLine(
                $"{i + 1:00}. {name}  " +
                $"({FormatTime(tracks[i].Sectors)})");
        }

        text.AppendLine();
        text.AppendLine($"Tracks: {tracks.Length}");
        text.AppendLine(
            $"Total track time: " +
            FormatTime(tracks.Sum(t => t.Sectors)));
        text.AppendLine(
            "Track times exclude the gaps between songs.");

        using var editor = new TrackListEditor(text.ToString());
        editor.ShowDialog(this);
    }
}

internal sealed class TrackListEditor : Form
{
    private readonly TextBox title = new()
    {
        Text = "My CD",
        Dock = DockStyle.Fill,
        MaxLength = 120
    };

    private readonly TextBox content = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Segoe UI", 10)
    };

    private readonly NumericUpDown width = Number(120, 50, 250);
    private readonly NumericUpDown height = Number(120, 50, 250);
    private readonly NumericUpDown titleSize = Number(16, 6, 36);
    private readonly NumericUpDown songSize = Number(9, 6, 24);

    private readonly CheckBox border = new()
    {
        Text = "Print cut border",
        Checked = true,
        AutoSize = true
    };

    private readonly Label warning = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true
    };

    private sealed record Cover(
        string Title,
        string Body,
        float Width,
        float Height,
        float TitleSize,
        float SongSize,
        bool Border);

    private static NumericUpDown Number(
        decimal value,
        decimal minimum,
        decimal maximum) => new()
    {
        Maximum = maximum,
        Minimum = minimum,
        Value = value,
        DecimalPlaces = 1,
        Increment = 0.5m,
        Width = 70
    };

    public TrackListEditor(string initialText)
    {
        Text = "CarDisc - Edit and print CD cover";
        ClientSize = new Size(950, 720);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        content.Text = initialText;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 7
        };

        foreach (float size in new float[] { 25, 35, 90, 45 })
        {
            layout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, size));
        }

        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 50));

        layout.Controls.Add(new Label
        {
            Text = "CD title",
            AutoSize = true
        }, 0, 0);

        layout.Controls.Add(title, 0, 1);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        var preset = new Button
        {
            Text = "CD cover: 120 x 120 mm",
            AutoSize = true
        };

        preset.Click += (_, _) =>
        {
            width.Value = 120;
            height.Value = 120;
        };

        options.Controls.Add(preset);
        AddOption(options, "Width (mm)", width);
        AddOption(options, "Height (mm)", height);
        AddOption(options, "Title (pt)", titleSize);
        AddOption(options, "Songs (pt)", songSize);
        options.Controls.Add(border);

        layout.Controls.Add(options, 0, 2);

        layout.Controls.Add(new Label
        {
            Text =
                "Edit this printout freely. Your playlist stays unchanged.\n" +
                "Track numbers and totals are text; update them yourself if you change the list.",
            Dock = DockStyle.Fill
        }, 0, 3);

        layout.Controls.Add(content, 0, 4);
        layout.Controls.Add(warning, 0, 5);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill
        };

        var preview = new Button
        {
            Text = "Preview cover",
            AutoSize = true
        };
        preview.Click += (_, _) => Preview();

        var print = new Button
        {
            Text = "Print / Save PDF",
            AutoSize = true
        };
        print.Click += (_, _) => PrintCover();

        var close = new Button
        {
            Text = "Close",
            AutoSize = true
        };
        close.Click += (_, _) => Close();

        actions.Controls.Add(preview);
        actions.Controls.Add(print);
        actions.Controls.Add(close);

        layout.Controls.Add(actions, 0, 6);
        Controls.Add(layout);

        title.TextChanged += (_, _) => UpdateWarning();
        content.TextChanged += (_, _) => UpdateWarning();

        foreach (var number in new[]
        {
            width, height, titleSize, songSize
        })
        {
            number.ValueChanged += (_, _) => UpdateWarning();
        }

        UpdateWarning();
    }

    private static void AddOption(
        FlowLayoutPanel panel,
        string label,
        Control control)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false
        };

        group.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0)
        });

        group.Controls.Add(control);
        panel.Controls.Add(group);
    }

    private Cover Snapshot() => new(
        title.Text.Trim(),
        content.Text.Replace("\r\n", "\n").TrimEnd(),
        (float)width.Value / 25.4f * 100,
        (float)height.Value / 25.4f * 100,
        (float)titleSize.Value,
        (float)songSize.Value,
        border.Checked);

    // Coordinates use hundredths of an inch.
    // The preview bitmap uses 100 DPI to match these units.
    private static void DrawCover(
        Graphics g,
        Cover cover,
        RectangleF box,
        bool draw)
    {
        const float padding = 20;

        var inner = new RectangleF(
            box.X + padding,
            box.Y + padding,
            box.Width - padding * 2,
            box.Height - padding * 2);

        using var headingFont = new Font(
            "Segoe UI", cover.TitleSize, FontStyle.Bold);

        using var songFont = new Font(
            "Segoe UI", cover.SongSize);

        using var format = new StringFormat(
            StringFormat.GenericDefault)
        {
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.LineLimit
        };

        float headingHeight = 0;

        if (cover.Title.Length > 0)
        {
            g.MeasureString(
                cover.Title,
                headingFont,
                inner.Size,
                format,
                out int titleFits,
                out int titleLines);

            if (titleFits < cover.Title.Length)
            {
                throw new InvalidOperationException(
                    "The title does not fit. " +
                    "Reduce its font size or shorten it.");
            }

            headingHeight = g.MeasureString(
                cover.Title,
                headingFont,
                new SizeF(inner.Width, 10000),
                format).Height;
        }

        float gap = headingHeight > 0 ? 10 : 0;

        var bodyArea = new RectangleF(
            inner.X,
            inner.Y + headingHeight + gap,
            inner.Width,
            inner.Height - headingHeight - gap);

        if (cover.Body.Length > 0)
        {
            if (bodyArea.Height <= 0)
            {
                throw new InvalidOperationException(
                    "The title leaves no room for songs. " +
                    "Reduce the title size.");
            }

            g.MeasureString(
                cover.Body,
                songFont,
                bodyArea.Size,
                format,
                out int bodyFits,
                out int bodyLines);

            if (bodyFits < cover.Body.Length)
            {
                throw new InvalidOperationException(
                    "The song list does not fit. " +
                    "Reduce Songs (pt), shorten the text, " +
                    "or increase the cover dimensions.");
            }
        }

        if (!draw)
            return;

        if (cover.Border)
        {
            using var pen = new Pen(Color.Gray, 0.5f);

            g.DrawRectangle(
                pen, box.X, box.Y, box.Width, box.Height);
        }

        if (headingHeight > 0)
        {
            g.DrawString(
                cover.Title,
                headingFont,
                Brushes.Black,
                new RectangleF(
                    inner.X,
                    inner.Y,
                    inner.Width,
                    headingHeight),
                format);
        }

        if (cover.Body.Length > 0)
        {
            g.DrawString(
                cover.Body,
                songFont,
                Brushes.Black,
                bodyArea,
                format);
        }
    }

    private static Bitmap MakePreview(Cover cover)
    {
        var image = new Bitmap(
            (int)Math.Ceiling(cover.Width) + 4,
            (int)Math.Ceiling(cover.Height) + 4);

        image.SetResolution(100, 100);

        try
        {
            using var g = Graphics.FromImage(image);
            g.Clear(Color.White);

            DrawCover(
                g,
                cover,
                new RectangleF(
                    2, 2, cover.Width, cover.Height),
                true);

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private void UpdateWarning()
    {
        try
        {
            using var image = MakePreview(Snapshot());

            warning.ForeColor = Color.DarkGreen;
            warning.Text =
                $"Text fits {width.Value:0.#} x {height.Value:0.#} mm. " +
                "Print at 100% / actual size; printer fit is checked separately.";
        }
        catch (Exception ex)
        {
            warning.ForeColor = Color.Firebrick;
            warning.Text = ex.Message;
        }
    }

    private void Preview()
    {
        try
        {
            using var image = MakePreview(Snapshot());

            using var window = new Form
            {
                Text =
                    $"Cover preview - " +
                    $"{width.Value:0.#} x {height.Value:0.#} mm",
                Width = 750,
                Height = 800,
                StartPosition = FormStartPosition.CenterParent
            };

            var picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = image,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.DimGray
            };

            var note = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Text =
                    "Preview is scaled to this window. " +
                    "Print dimensions use the chosen millimeters.\n" +
                    "Close this window to edit or choose Print / Save PDF."
            };

            window.Controls.Add(picture);
            window.Controls.Add(note);

            window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void PrintCover()
    {
        try
        {
            var cover = Snapshot();

            // Validate that all text fits before opening the printer.
            using var check = MakePreview(cover);

            using var document = new PrintDocument
            {
                DocumentName = "CarDisc CD cover",
                OriginAtMargins = true
            };

            document.DefaultPageSettings.Margins =
                new Margins(0, 0, 0, 0);

            using var dialog = new PrintDialog
            {
                Document = document,
                UseEXDialog = true,
                AllowSelection = false,
                AllowSomePages = false
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            // Center the cover on the physical sheet.
            var page = document.DefaultPageSettings;

            var box = new RectangleF(
                (page.Bounds.Width - cover.Width) / 2,
                (page.Bounds.Height - cover.Height) / 2,
                cover.Width,
                cover.Height);

            var printable = page.PrintableArea;

            if (box.Left < printable.Left + 1 ||
                box.Top < printable.Top + 1 ||
                box.Right > printable.Right - 1 ||
                box.Bottom > printable.Bottom - 1)
            {
                throw new InvalidOperationException(
                    "This cover does not fit the selected paper's " +
                    "printable area. Choose larger paper in printer " +
                    "Properties or reduce the cover dimensions.");
            }

            document.PrintPage += (sender, e) =>
            {
                var g = e.Graphics
                    ?? throw new InvalidOperationException(
                        "No print surface available.");

                DrawCover(g, cover, box, true);
                e.HasMorePages = false;
            };

            document.Print();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ShowError(Exception ex)
    {
        MessageBox.Show(
            this,
            ex.Message,
            "CD cover",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}