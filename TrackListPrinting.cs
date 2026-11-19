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
                this,
                "Add some songs first.",
                "Print track list");
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
        WordWrap = true,
        Font = new Font("Consolas", 11)
    };

    public TrackListEditor(string initialText)
    {
        Text = "CarDisc - Edit and print track list";
        ClientSize = new Size(850, 650);
        MinimumSize = new Size(650, 450);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);

        content.Text = initialText;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };

        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(new Label
        {
            Text = "CD title",
            AutoSize = true
        }, 0, 0);

        layout.Controls.Add(title, 0, 1);

        layout.Controls.Add(new Label
        {
            Text =
                "Edit the text below. Changes affect only this printout.\n" +
                "Totals are editable text; they do not recalculate after edits.",
            Dock = DockStyle.Fill
        }, 0, 2);

        layout.Controls.Add(content, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0)
        };

        var preview = new Button
        {
            Text = "Print preview",
            AutoSize = true
        };
        preview.Click += (_, _) => ShowPreview();

        var print = new Button
        {
            Text = "Print / Save PDF",
            AutoSize = true
        };
        print.Click += (_, _) => PrintCurrent();

        var close = new Button
        {
            Text = "Close",
            AutoSize = true
        };
        close.Click += (_, _) => Close();

        actions.Controls.Add(preview);
        actions.Controls.Add(print);
        actions.Controls.Add(close);

        layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
    }

    private PrintDocument CreateDocument(out Func<int> pageCount)
    {
        // Capture the current edits for this preview or print job.
        string heading = string.IsNullOrWhiteSpace(title.Text)
            ? "Track list"
            : title.Text.Trim();

        string body = content.Text.Replace("\r\n", "\n");

        int position = 0;
        int page = 0;

        pageCount = () => page;

        var document = new PrintDocument
        {
            DocumentName = "CarDisc track list",
            OriginAtMargins = false
        };

        document.DefaultPageSettings.Margins =
            new Margins(60, 60, 60, 60);

        document.BeginPrint += (_, _) =>
        {
            position = 0;
            page = 0;
        };

        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics
                ?? throw new InvalidOperationException(
                    "Windows did not provide a print surface.");

            page++;

            var bounds = e.MarginBounds;

            using var headingFont =
                new Font("Segoe UI", 18, FontStyle.Bold);
            using var bodyFont =
                new Font("Consolas", 10);
            using var footerFont =
                new Font("Segoe UI", 9);

            using var headingFormat = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter
            };

            using var bodyFormat =
                new StringFormat(StringFormat.GenericDefault);

            float headingHeight =
                headingFont.GetHeight(graphics) * 2 + 12;

            float footerHeight =
                footerFont.GetHeight(graphics) + 12;

            var area = new RectangleF(
                bounds.Left,
                bounds.Top + headingHeight,
                bounds.Width,
                bounds.Height - headingHeight - footerHeight);

            if (area.Width <= 0 ||
                area.Height < bodyFont.GetHeight(graphics))
            {
                throw new InvalidOperationException(
                    "The selected paper is too small. " +
                    "Choose Letter or A4 paper.");
            }

            graphics.DrawString(
                heading,
                headingFont,
                Brushes.Black,
                new RectangleF(
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    headingHeight - 8),
                headingFormat);

            if (position < body.Length)
            {
                string remaining = body.Substring(position);

                graphics.MeasureString(
                    remaining,
                    bodyFont,
                    area.Size,
                    bodyFormat,
                    out int fitted,
                    out int linesFitted);

                if (fitted <= 0)
                {
                    throw new InvalidOperationException(
                        "The text could not fit on the selected paper.");
                }

                graphics.DrawString(
                    remaining.Substring(0, fitted),
                    bodyFont,
                    Brushes.Black,
                    area,
                    bodyFormat);

                position += fitted;
            }

            graphics.DrawString(
                $"CarDisc | Page {page}",
                footerFont,
                Brushes.Gray,
                bounds.Left,
                bounds.Bottom - footerHeight + 6);

            e.HasMorePages = position < body.Length;
        };

        return document;
    }

    private void PrintCurrent()
    {
        try
        {
            using var document = CreateDocument(out _);
            ChoosePrinter(document, this);
        }
        catch (Exception ex)
        {
            ShowPrintError(ex);
        }
    }

    private void ShowPreview()
    {
        try
        {
            using var document =
                CreateDocument(out var pageCount);

            if (!document.PrinterSettings.IsValid)
            {
                throw new InvalidOperationException(
                    "No default printer is available. " +
                    "Install or enable a printer, such as " +
                    "Microsoft Print to PDF, in Windows settings.");
            }

            using var window = new Form
            {
                Text = "CarDisc - Print preview",
                Width = 1000,
                Height = 750,
                StartPosition = FormStartPosition.CenterParent
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };

            layout.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));

            var controls = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill
            };

            var preview = new PrintPreviewControl
            {
                Dock = DockStyle.Fill,
                Document = document,
                AutoZoom = true,
                UseAntiAlias = true
            };

            var print = new Button
            {
                Text = "Print / Save PDF",
                AutoSize = true
            };

            print.Click += (_, _) =>
            {
                try
                {
                    ChoosePrinter(document, window);
                    preview.InvalidatePreview();
                }
                catch (Exception ex)
                {
                    ShowPrintError(ex);
                }
            };

            var previous = new Button
            {
                Text = "Previous page",
                AutoSize = true
            };

            previous.Click += (_, _) =>
                preview.StartPage =
                    Math.Max(0, preview.StartPage - 1);

            var next = new Button
            {
                Text = "Next page",
                AutoSize = true
            };

            next.Click += (_, _) =>
                preview.StartPage = Math.Min(
                    Math.Max(0, pageCount() - 1),
                    preview.StartPage + 1);

            var close = new Button
            {
                Text = "Close preview",
                AutoSize = true
            };

            close.Click += (_, _) => window.Close();

            controls.Controls.Add(print);
            controls.Controls.Add(previous);
            controls.Controls.Add(next);
            controls.Controls.Add(close);

            layout.Controls.Add(controls, 0, 0);
            layout.Controls.Add(preview, 0, 1);

            window.Controls.Add(layout);
            window.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowPrintError(ex);
        }
    }

    private static void ChoosePrinter(
        PrintDocument document,
        IWin32Window owner)
    {
        using var dialog = new PrintDialog
        {
            Document = document,
            UseEXDialog = true,
            AllowSomePages = false,
            AllowSelection = false
        };

        if (dialog.ShowDialog(owner) == DialogResult.OK)
            document.Print();
    }

    private void ShowPrintError(Exception ex)
    {
        MessageBox.Show(
            this,
            ex.Message,
            "Could not print track list",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}