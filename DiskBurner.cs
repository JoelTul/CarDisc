using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace CarDisc;

public static class DiscBurner
{
    public static Task BurnAsync(
        DiscWriter writer,
        string[] tracks,
        long capacitySectors,
        IProgress<string> progress)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                Burn(writer, tracks, capacitySectors, progress);
                completion.SetResult(true);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    private static void Burn(
        DiscWriter writer,
        string[] tracks,
        long capacitySectors,
        IProgress<string> progress)
    {
        if (tracks.Length is < 1 or > 99)
            throw new InvalidOperationException(
                "Choose between 1 and 99 tracks.");

        string folder = Path.Combine(
            Path.GetTempPath(),
            "CarDisc",
            Guid.NewGuid().ToString("N"));

        dynamic? recorder = null;
        dynamic? format = null;
        bool prepared = false;
        string step = "Starting";

        try
        {
            step = "Creating temporary audio folder";
            Directory.CreateDirectory(folder);

            var files = new List<string>();
            long requiredSectors = 0;

            for (int i = 0; i < tracks.Length; i++)
            {
                step = $"Converting track {i + 1}: " +
                    Path.GetFileName(tracks[i]);

                progress.Report(step);

                string output = Path.Combine(folder, $"{i:00}.pcm");

                long sectors = ConvertAudio(tracks[i], output);
                requiredSectors += sectors + 150;
                files.Add(output);

                if (requiredSectors > capacitySectors)
                {
                    throw new InvalidOperationException(
                        "The audio exceeds the selected CD capacity. " +
                        "Remove a track and try again.");
                }
            }

            step = "Creating Windows disc recorder";
            recorder = CreateCom("IMAPI2.MsftDiscRecorder2");

            step = "Connecting to selected drive";
            recorder.InitializeDiscRecorder(writer.Id);

            step = "Creating audio-CD writer";
            format = CreateCom("IMAPI2.MsftDiscFormat2TrackAtOnce");

            step = "Assigning recorder";
            format.Recorder = recorder;

            step = "Setting application name";
            format.ClientName = "CarDisc";

            // This setting must be assigned BEFORE PrepareMedia.
            step = "Setting disc finalization";
            // Use the default finalization setting.

            // PrepareMedia itself validates that the inserted disc
            // is supported blank CD-R/RW media.
            step = "Preparing blank CD";
            progress.Report(
                "Preparing blank CD. Keep the drive connected…");

            format.PrepareMedia();
            prepared = true;

            step = "Reading prepared disc capacity";
            long availableSectors =
                Convert.ToInt64(format.FreeSectorsOnMedia);

            if (requiredSectors > availableSectors)
            {
                throw new InvalidOperationException(
                    "The playlist exceeds this disc's available space. " +
                    "Remove a track and try again.");
            }

            for (int i = 0; i < files.Count; i++)
            {
                step = $"Opening prepared audio for track {i + 1}";

                int result = SHCreateStreamOnFileEx(
                    files[i],
                    0x00000020,
                    0,
                    false,
                    null,
                    out IStream stream);

                Marshal.ThrowExceptionForHR(result);

                try
                {
                    step = $"Writing track {i + 1} of {files.Count}";
                    progress.Report(step + ". Keep the drive connected…");

                    format.AddAudioTrack(stream);
                }
                finally
                {
                    ReleaseCom(stream);
                }
            }

            step = "Finalizing and releasing CD";
            progress.Report("Finalizing the CD for playback…");

            format.ReleaseMedia();
            prepared = false;
        }
        catch (Exception ex)
        {
            throw new COMException(
                $"Failed during: {step}\n\n{ex.Message}",
                ex.HResult);
        }
        finally
        {
            if (prepared)
            {
                try
                {
                    format!.ReleaseMedia();
                }
                catch
                {
                    // Preserve the original failure.
                }
            }

            ReleaseCom(format);
            ReleaseCom(recorder);

            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch
            {
                // Windows may temporarily retain a file lock.
            }
        }
    }

    private static long ConvertAudio(
        string source,
        string destination)
    {
        using var reader = new MediaFoundationReader(source);

        using var resampler = new MediaFoundationResampler(
            reader,
            new WaveFormat(44100, 16, 2))
        {
            ResamplerQuality = 60
        };

        using var output = File.Create(destination);

        var buffer = new byte[65536];
        int count;

        while ((count = resampler.Read(
            buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, count);
        }

        if (output.Length == 0)
        {
            throw new InvalidDataException(
                "No audio could be decoded from " +
                Path.GetFileName(source));
        }

        long sectors = Math.Max(
            300L,
            (output.Length + 2351) / 2352);

        output.SetLength(sectors * 2352);
        return sectors;
    }

    private static object CreateCom(string name)
    {
        var type = Type.GetTypeFromProgID(
            name,
            throwOnError: true)!;

        return Activator.CreateInstance(type)!;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }

    [DllImport(
        "shlwapi.dll",
        CharSet = CharSet.Unicode,
        PreserveSig = true)]
    private static extern int SHCreateStreamOnFileEx(
        string path,
        uint mode,
        uint attributes,
        [MarshalAs(UnmanagedType.Bool)] bool create,
        IStream? template,
        out IStream stream);
}