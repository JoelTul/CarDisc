using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CarDisc;

public sealed record DiscWriter(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class DiscDrives
{
    private static object CreateCom(string name)
    {
        var type = Type.GetTypeFromProgID(name, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    public static List<DiscWriter> FindWriters()
    {
        var writers = new List<DiscWriter>();
        dynamic? master = null;

        try
        {
            master = CreateCom("IMAPI2.MsftDiscMaster2");

            if (!(bool)master.IsSupportedEnvironment)
                return writers;

            int count = (int)master.Count;

            for (int i = 0; i < count; i++)
            {
                dynamic? recorder = null;
                dynamic? format = null;

                try
                {
                    string id = (string)master[i];

                    recorder = CreateCom("IMAPI2.MsftDiscRecorder2");
                    recorder.InitializeDiscRecorder(id);

                    format = CreateCom("IMAPI2.MsftDiscFormat2TrackAtOnce");

                    if (!(bool)format.IsRecorderSupported(recorder))
                        continue;

                    var paths = new List<string>();

                    foreach (object path in (Array)recorder.VolumePathNames)
                        paths.Add(path.ToString() ?? "");

                    string name =
                        $"{string.Join(" ", paths)}  " +
                        $"{recorder.VendorId} {recorder.ProductId}";

                    writers.Add(new DiscWriter(id, name.Trim()));
                }
                finally
                {
                    ReleaseCom(format);
                    ReleaseCom(recorder);
                }
            }

            return writers;
        }
        finally
        {
            ReleaseCom(master);
        }
    }

    public static DiscWriter? ChooseWriter(IWin32Window owner)
    {
        var writers = FindWriters();

        if (writers.Count == 0)
        {
            MessageBox.Show(
                owner,
                "No compatible audio-CD writer was found.\n\n" +
                "Connect your drive and try again. " +
                "The drive must support writing CDs.",
                "CarDisc",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return null;
        }

        using var dialog = new Form
        {
            Text = "Choose your CD writer",
            ClientSize = new Size(580, 140),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("Segoe UI", 10)
        };

        var selector = new ComboBox
        {
            Left = 20,
            Top = 20,
            Width = 540,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        foreach (var writer in writers)
            selector.Items.Add(writer);

        selector.SelectedIndex = 0;

        var confirm = new Button
        {
            Text = "Use this drive",
            Left = 330,
            Top = 80,
            Width = 125,
            Height = 32,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Left = 465,
            Top = 80,
            Width = 95,
            Height = 32,
            DialogResult = DialogResult.Cancel
        };

        dialog.Controls.Add(selector);
        dialog.Controls.Add(confirm);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(owner) == DialogResult.OK
            ? selector.SelectedItem as DiscWriter
            : null;
    }
}