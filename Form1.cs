using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace CarDisc;

public partial class Form1 : Form
{
    private readonly ListBox playlist = new()
    {
        Dock = DockStyle.Fill,
        HorizontalScrollbar = true
    };

    private readonly Label status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private readonly ProgressBar meter = new()
    {
        Dock = DockStyle.Fill,
        Maximum = 1000
    };

    private readonly ComboBox capacity = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 150
    };

    private readonly Label summary = new()
    {
        AutoSize = true,
        Padding = new Padding(10, 5, 0, 0)
    };

    private readonly Label driveLabel = new()
    {
        Text = "CD writer: none selected",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private bool readingFiles;
    private DiscWriter? selectedWriter;

    public Form1()
    {
        InitializeComponent();

        Text = "CarDisc — Music for the road";
        ClientSize = new Size(1050, 650);
        MinimumSize = new Size(850, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 7
        };

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var heading = new Label
        {
            Text = "CarDisc",
            Font = new Font("Segoe UI", 24, FontStyle.Bold),
            AutoSize = true
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = true
        };

        buttons.Controls.Add(MakeButton("Add music", async () =>
        {
            await ChooseMusic();
        }));

        buttons.Controls.Add(MakeButton(
            "Move up", () => MoveTrack(-1)));

        buttons.Controls.Add(MakeButton(
            "Move down", () => MoveTrack(1)));

        buttons.Controls.Add(MakeButton(
            "Remove", RemoveTrack));

        buttons.Controls.Add(MakeButton("Clear", () =>
        {
            playlist.Items.Clear();
            UpdateCapacity();
        }));

        buttons.Controls.Add(MakeButton(
            "CD writer", ChooseWriter));

        buttons.Controls.Add(MakeButton(
            "Burn CD", async () => await BurnCd()));

        capacity.Items.AddRange(new object[]
        {
            "80-minute CD",
            "74-minute CD"
        });

        capacity.SelectedIndex = 0;
        capacity.SelectedIndexChanged += (_, _) => UpdateCapacity();

        var capacityRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0)
        };

        capacityRow.Controls.Add(capacity);
        capacityRow.Controls.Add(summary);

        playlist.AllowDrop = true;

        playlist.DragEnter += (_, e) =>
        {
            e.Effect = !readingFiles &&
                       e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        };

        playlist.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                await AddFiles(paths);
        };

        FormClosing += (_, e) =>
        {
            if (readingFiles)
                e.Cancel = true;
        };

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(playlist, 0, 2);
        layout.Controls.Add(capacityRow, 0, 3);
        layout.Controls.Add(meter, 0, 4);
        layout.Controls.Add(driveLabel, 0, 5);
        layout.Controls.Add(status, 0, 6);

        Controls.Add(layout);
        UpdateCapacity();
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34
        };

        button.Click += (_, _) => action();
        return button;
    }

    private void ChooseWriter()
    {
        try
        {
            var writer = DiscDrives.ChooseWriter(this);

            if (writer is not null)
            {
                selectedWriter = writer;
                driveLabel.Text = $"CD writer: {writer.Name}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"{ex.Message}\n\nError code: 0x{ex.HResult:X8}",
                "Could not access CD writers",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task BurnCd()
    {
        if (readingFiles)
            return;

        if (selectedWriter is null)
        {
            ChooseWriter();

            if (selectedWriter is null)
                return;
        }

        var tracks = playlist.Items
            .Cast<MusicFile>()
            .ToArray();

        if (tracks.Length is < 1 or > 99)
        {
            MessageBox.Show(
                this,
                "Add between 1 and 99 tracks before burning.",
                "CarDisc");
            return;
        }

        long limit =
            (capacity.SelectedIndex == 1 ? 74 : 80) * 60L * 75;

        long estimatedSectors = tracks.Sum(t => t.Sectors + 150);

        if (estimatedSectors > limit)
        {
            MessageBox.Show(
                this,
                "The playlist exceeds your selected CD capacity. " +
                "Remove a track before burning.",
                "CarDisc");
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Burn {tracks.Length} tracks to:\n\n" +
            $"{selectedWriter.Name}\n\n" +
            "Insert a blank CD-R before continuing.\n\n" +
            "Writing permanently uses the disc. Keep the computer awake " +
            "and the drive connected until the operation finishes.",
            "Burn audio CD",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (confirmation != DialogResult.OK)
            return;

        readingFiles = true;
        Enabled = false;
        UseWaitCursor = true;
        meter.Style = ProgressBarStyle.Marquee;

        bool acceptingProgress = true;

        var progress = new Progress<string>(message =>
        {
            if (acceptingProgress)
                status.Text = message;
        });

        try
        {
            await DiscBurner.BurnAsync(
                selectedWriter,
                tracks.Select(t => t.FullPath).ToArray(),
                limit,
                progress);

            acceptingProgress = false;
            status.Text = "Burn complete.";

            MessageBox.Show(
                this,
                "Windows reports that the CD was burned and finalized.\n\n" +
                "Try it in your car to confirm playback.",
                "CarDisc",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            acceptingProgress = false;
            status.Text = "The burn did not complete.";

            MessageBox.Show(
                this,
                $"{ex.Message}\n\n" +
                $"Error code: 0x{ex.HResult:X8}\n\n" +
                "If writing had started, use a fresh blank CD " +
                "for the next attempt.",
                "Burn failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            acceptingProgress = false;
            readingFiles = false;
            Enabled = true;
            UseWaitCursor = false;
            meter.Style = ProgressBarStyle.Continuous;
        }
    }

    private async Task ChooseMusic()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose your music",
            Multiselect = true,
            Filter =
                "Music files|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.aiff|" +
                "All files|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            await AddFiles(dialog.FileNames);
    }

    private async Task AddFiles(string[] paths)
    {
        if (readingFiles)
            return;

        readingFiles = true;
        Enabled = false;
        UseWaitCursor = true;
        status.Text = "Reading track lengths…";

        var errors = new List<string>();

        try
        {
            var tracks = await Task.Run(() =>
            {
                var result = new List<MusicFile>();

                foreach (string path in paths)
                {
                    try
                    {
                        using var audio = new MediaFoundationReader(path);
                        double seconds = audio.TotalTime.TotalSeconds;

                        if (seconds <= 0 ||
                            double.IsNaN(seconds) ||
                            double.IsInfinity(seconds))
                        {
                            throw new InvalidDataException(
                                "No usable audio duration was found.");
                        }

                        // Audio CDs use 75 sectors per second.
                        // Reserve a minimum of four seconds per track.
                        long sectors = Math.Max(
                            300L,
                            (long)Math.Ceiling(seconds * 75));

                        result.Add(new MusicFile(path, sectors));
                    }
                    catch (Exception ex)
                    {
                        errors.Add(
                            $"{Path.GetFileName(path)}: {ex.Message}");
                    }
                }

                return result;
            });

            foreach (var track in tracks)
                playlist.Items.Add(track);

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, errors),
                    "Some files could not be opened",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Could not read music",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            readingFiles = false;
            Enabled = true;
            UseWaitCursor = false;
            UpdateCapacity();
        }
    }

    private void MoveTrack(int direction)
    {
        int current = playlist.SelectedIndex;
        int target = current + direction;

        if (current < 0 || target < 0 || target >= playlist.Items.Count)
            return;

        object item = playlist.Items[current];
        playlist.Items.RemoveAt(current);
        playlist.Items.Insert(target, item);
        playlist.SelectedIndex = target;
    }

    private void RemoveTrack()
    {
        if (playlist.SelectedIndex >= 0)
            playlist.Items.RemoveAt(playlist.SelectedIndex);

        UpdateCapacity();
    }

    private void UpdateCapacity()
    {
        long limit =
            (capacity.SelectedIndex == 1 ? 74 : 80) * 60L * 75;

        // Conservatively allow two seconds of spacing per track.
        long used = playlist.Items
            .Cast<MusicFile>()
            .Sum(track => track.Sectors + 150);

        int count = playlist.Items.Count;
        bool fits = used <= limit && count <= 99;

        summary.Text =
            $"{count} tracks  •  {FormatTime(used)} / {FormatTime(limit)}";

        summary.ForeColor =
            fits ? SystemColors.ControlText : Color.Firebrick;

        meter.Value =
            (int)Math.Clamp(used * 1000 / limit, 0L, 1000L);

        status.Text = count == 0
            ? "Add music files or drag them into the playlist."
            : count > 99
                ? "Audio CDs support up to 99 tracks. Remove some tracks."
                : used > limit
                    ? $"Over capacity by {FormatTime(used - limit)}. Remove a track."
                    : $"Fits with about {FormatTime(limit - used)} remaining, including gap allowance.";
    }

    private static string FormatTime(long sectors)
    {
        long seconds = (sectors + 74) / 75;
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private sealed record MusicFile(string FullPath, long Sectors)
    {
        public override string ToString() =>
            $"{FormatTime(Sectors)}    " +
            Path.GetFileNameWithoutExtension(FullPath);
    }
}