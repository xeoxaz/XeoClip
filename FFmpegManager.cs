using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace XeoClip
{
	internal class FFmpegManager
	{
		private Process? ffmpegProcess;
		private DateTime? startTime;
		private string? videoFile;
		private Thread? recordingThread;
		private readonly string recordingsFolder;
		private readonly string ffmpegPath;

		public FFmpegManager(string baseDirectory)
		{
			// Ensure the recordings folder exists
			recordingsFolder = Path.Combine(baseDirectory, "recordings");
			Directory.CreateDirectory(recordingsFolder);

			// Ensure FFmpeg is installed and accessible
			ffmpegPath = "ffmpeg"; // Default to "ffmpeg", adjust if installed in a custom path
			if (!IsFFmpegAvailable())
			{
				throw new FileNotFoundException("FFmpeg executable not found. Please ensure FFmpeg is installed and added to the system PATH.");
			}
		}

		public void StartRecording()
		{
			if (ffmpegProcess != null)
			{
				Console.WriteLine("Recording is already in progress.");
				return;
			}

			Console.WriteLine("Starting video recording...");
			Console.Beep(1000, 500);

			videoFile = GenerateOutputFileName("video", "mp4");
			startTime = DateTime.Now;

			string ffmpegCommand = GetFFmpegCommand(videoFile);

			recordingThread = new Thread(() =>
			{
				try
				{
					ffmpegProcess = RunFFmpeg(ffmpegCommand);
					Thread.CurrentThread.Priority = ThreadPriority.Highest;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error starting recording: {ex.Message}");
				}
			})
			{
				IsBackground = true
			};

			recordingThread.Start();
		}

		public void StopRecording()
		{
			if (ffmpegProcess == null || ffmpegProcess.HasExited)
			{
				Console.WriteLine("No active recording to stop.");
				return;
			}

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
			if (string.IsNullOrEmpty(videoFile))
			{
				Console.WriteLine("No video file found to merge.");
				return;
			}
			if (string.IsNullOrEmpty(audioFile) || !File.Exists(audioFile))
			{
				Console.WriteLine("Audio file is missing or invalid.");
				return;
			}

			string outputFile = GenerateOutputFileName("final", "mp4");
			string mergeCommand = $"{ffmpegPath} -i \"{Path.Combine(recordingsFolder, videoFile)}\" -i \"{audioFile}\" -c:v copy -c:a aac \"{outputFile}\"";

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

		public void ClipVideo(double startTimeInSeconds, double endTimeInSeconds)
		{
			if (string.IsNullOrEmpty(videoFile) || !File.Exists(Path.Combine(recordingsFolder, videoFile)))
			{
				Console.WriteLine("No video file found to clip.");
				return;
			}

			string inputFile = Path.Combine(recordingsFolder, videoFile);
			string outputFile = Path.Combine(recordingsFolder, $"clip_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");

			string clipCommand = $"{ffmpegPath} -i \"{inputFile}\" -ss {startTimeInSeconds} -to {endTimeInSeconds} -c:v copy -c:a copy \"{outputFile}\"";
			Console.WriteLine($"Clipping video: {clipCommand}");
			Process ffmpegProcess = RunFFmpeg(clipCommand);

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
					Console.WriteLine($"Clipped video saved to: {outputFile}");
				}
			}
		}

		public string GetRecordingTime()
		{
			return startTime.HasValue ? $"{(DateTime.Now - startTime.Value):mm\\:ss}" : "Not recording";
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

		private string GenerateOutputFileName(string prefix, string extension)
			=> Path.Combine(recordingsFolder, $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}");

		private string GetFFmpegCommand(string outputFile)
			=> $"{ffmpegPath} -hwaccel cuda -loglevel error -nostats -hide_banner -f gdigrab " +
			   $"-framerate 60 -video_size 1920x1080 -offset_x 0 -offset_y 0 -rtbufsize 100M " +
			   $"-i desktop -c:v h264_nvenc -preset p1 -pix_fmt yuv420p -rc:v vbr_hq -cq:v 21 " +
			   $"-b:v 8M -maxrate:v 16M -bufsize:v 32M -vsync cfr -f mp4 \"{outputFile}\"";

		private void Cleanup()
		{
			try
			{
				ffmpegProcess?.Dispose();
				ffmpegProcess = null;
				startTime = null;
				recordingThread?.Join();
				recordingThread = null;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error during cleanup: {ex.Message}");
			}
		}

		private bool IsFFmpegAvailable()
		{
			try
			{
				ProcessStartInfo startInfo = new()
				{
					FileName = ffmpegPath,
					Arguments = "-version",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using Process process = Process.Start(startInfo);
				process.WaitForExit();
				return process.ExitCode == 0;
			}
			catch
			{
				return false;
			}
		}
	}
}