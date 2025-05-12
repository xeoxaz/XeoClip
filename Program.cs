using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.VisualBasic;
using NAudio.Wave;

namespace XeoClip
{
	internal class Program
	{
		private static readonly FFmpegManager ffmpegManager = new();
		private static readonly AudioManager audioManager = new();
		private static int selectedIndex = 0;
		private static readonly string[] options = { "Start Recording", "Stop Recording", "Exit" };

		static void Main()
		{
			EnsureDirectoryExists(@"C:\test\audio");
			EnsureDirectoryExists(@"C:\test\video");
			AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();

			while (true)
			{
				RenderMenu();
				ProcessInput();
			}
		}

		private static void EnsureDirectoryExists(string path) => Directory.CreateDirectory(path);

		private static void RenderMenu()
		{
			Console.Clear();
			Console.WriteLine("Use ↑ ↓ arrows to navigate. Press Enter to select:");
			Console.WriteLine($"Recording Time: {ffmpegManager.GetRecordingTime()}");

			for (int i = 0; i < options.Length; i++)
			{
				Console.BackgroundColor = i == selectedIndex ? ConsoleColor.White : ConsoleColor.Black;
				Console.ForegroundColor = i == selectedIndex ? ConsoleColor.Black : ConsoleColor.Gray;
				Console.WriteLine($"{(i == selectedIndex ? "> " : "  ")}{options[i]}");
				Console.ResetColor();
			}
		}

		private static void ProcessInput()
		{
			switch (Console.ReadKey(true).Key)
			{
				case ConsoleKey.UpArrow: selectedIndex = Math.Max(selectedIndex - 1, 0); break;
				case ConsoleKey.DownArrow: selectedIndex = Math.Min(selectedIndex + 1, options.Length - 1); break;
				case ConsoleKey.Enter: HandleSelection(); break;
			}
		}

		private static void HandleSelection()
		{
			switch (options[selectedIndex])
			{
				case "Start Recording":
					ffmpegManager.StartRecording();
					audioManager.StartRecording();
					break;
				case "Stop Recording":
					ffmpegManager.StopRecording();
					audioManager.StopRecording();
					ffmpegManager.MergeAudioAndVideo(audioManager.AudioFilePath);
					break;
				case "Exit":
					Console.WriteLine("Exiting program...");
					StopAll();
					Environment.Exit(0);
					break;
			}
		}

		private static void StopAll()
		{
			ffmpegManager.StopRecording();
			audioManager.StopRecording();
		}
	}

	internal class FFmpegManager
	{
		private Process? ffmpegProcess;
		private DateTime? startTime;
		private string? videoFile;
		private Thread? recordingThread;

		public void StartRecording()
		{
			if (ffmpegProcess != null) { Console.WriteLine("Recording already in progress..."); return; }

			Console.WriteLine("Starting video recording...");
			Console.Beep(1000, 500);

			videoFile = GenerateOutputFileName(@"C:\test\video", "video", "mp4");
			startTime = DateTime.Now;

			string ffmpegCommand = GetFFmpegCommand();

			recordingThread = new Thread(() =>
			{
				ffmpegProcess = RunFFmpeg(ffmpegCommand);
				Thread.CurrentThread.Priority = ThreadPriority.Highest;
			});

			recordingThread.Start();
		}

		public void StopRecording()
		{
			if (ffmpegProcess == null || ffmpegProcess.HasExited) { Console.WriteLine("No active recording to stop."); return; }

			try
			{
				Console.WriteLine("Stopping video recording...");
				Console.Beep(800, 500);
				ffmpegProcess.StandardInput.WriteLine("q");
				ffmpegProcess.StandardInput.Flush();

				// Wait for FFmpeg to exit naturally
				if (!ffmpegProcess.WaitForExit(3000))
				{
					Console.WriteLine("Forcing video recording shutdown...");
					ffmpegProcess.Kill();
				}

				Console.WriteLine($"Video saved to: {videoFile}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error stopping video recording: {ex.Message}");
			}
			finally
			{
				Cleanup();
			}
		}

		public void MergeAudioAndVideo(string audioFile)
		{
			if (string.IsNullOrEmpty(videoFile) || string.IsNullOrEmpty(audioFile))
			{
				Console.WriteLine("Video or audio file is missing.");
				return;
			}

			string outputFile = GenerateOutputFileName(@"C:\test", "final", "mp4");
			string mergeCommand = $"ffmpeg -i \"{videoFile}\" -i \"{audioFile}\" -c:v copy -c:a aac \"{outputFile}\"";

			Console.WriteLine("Merging video and audio...");
			Process ffmpegProcess = RunFFmpeg(mergeCommand);

			if (ffmpegProcess != null)
			{
				string errorOutput = ffmpegProcess.StandardError.ReadToEnd();
				ffmpegProcess.WaitForExit();

				if (ffmpegProcess.ExitCode != 0)
				{
					Console.WriteLine($"FFmpeg Error: {errorOutput}");
				}
				else
				{
					Console.WriteLine($"Merged file saved to: {outputFile}");
				}
			}
		}

		private Process RunFFmpeg(string arguments)
		{
			ProcessStartInfo startInfo = new()
			{
				FileName = "cmd.exe",
				Arguments = $"/C {arguments}",
				RedirectStandardInput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			Process process = new() { StartInfo = startInfo };
			process.Start();
			process.PriorityClass = ProcessPriorityClass.High;
			return process;
		}

		private static string GenerateOutputFileName(string directory, string prefix, string extension)
			=> Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}");

		private string GetFFmpegCommand() => $"ffmpeg -hwaccel cuda -loglevel error -nostats -hide_banner -f gdigrab -framerate 60 -video_size 1920x1080 -offset_x 0 -offset_y 0 -rtbufsize 100M -i desktop -c:v h264_nvenc -preset p1 -pix_fmt yuv420p -rc:v vbr_hq -cq:v 21 -b:v 8M -maxrate:v 16M -bufsize:v 32M -vsync cfr -f mp4 \"{videoFile}\" > nul 2>&1";

		public string GetRecordingTime() => startTime.HasValue ? $"{(DateTime.Now - startTime.Value):mm\\:ss}" : "Not recording";

		private void Cleanup()
		{
			ffmpegProcess?.Dispose();
			ffmpegProcess = null;
			startTime = null;
			recordingThread?.Join();
			recordingThread = null;
		}
	}

	internal class AudioManager
	{
		private WaveInEvent? waveIn;
		private WaveFileWriter? waveFileWriter;
		public string? AudioFilePath { get; private set; }

		public void StartRecording()
		{
			Console.WriteLine("Starting audio recording...");
			AudioFilePath = GenerateOutputFileName(@"C:\test\audio", "audio", "wav");

			waveIn = new WaveInEvent
			{
				DeviceNumber = 0, // Default audio input device
				WaveFormat = new WaveFormat(44100, 1) // 44.1kHz, mono
			};

			waveIn.DataAvailable += (s, e) =>
			{
				waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);
			};

			waveFileWriter = new WaveFileWriter(AudioFilePath, waveIn.WaveFormat);
			waveIn.StartRecording();
		}

		public void StopRecording()
		{
			if (waveIn == null) { Console.WriteLine("No active audio recording to stop."); return; }

			try
			{
				Console.WriteLine("Stopping audio recording...");
				waveIn.StopRecording();
				waveFileWriter?.Dispose();
				Console.WriteLine($"Audio saved to: {AudioFilePath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error stopping audio recording: {ex.Message}");
			}
			finally
			{
				waveIn?.Dispose();
				waveIn = null;
				waveFileWriter = null;
			}
		}

		private static string GenerateOutputFileName(string directory, string prefix, string extension)
			=> Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}");
	}
}