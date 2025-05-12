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
		private string? videoFilePath;
		private Thread? recordingThread;
		private readonly string baseDirectory;

		public FFmpegManager(string baseDirectory)
		{
			this.baseDirectory = baseDirectory;
		}

		public void StartRecording(string timestampDir)
		{
			if (ffmpegProcess != null)
			{
				Console.WriteLine("Recording is already in progress.");
				return;
			}

			Console.WriteLine("Starting video recording...");
			Console.Beep(1000, 500);

			// Generate the video file path in the timestamped directory
			videoFilePath = GenerateOutputFileName(timestampDir, "video", "mp4");
			startTime = DateTime.Now;

			string ffmpegCommand = GetFFmpegCommand(videoFilePath);

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

				if (!ffmpegProcess.WaitForExit(3000))
				{
					Console.WriteLine("Forcing video recording shutdown...");
					ffmpegProcess.Kill();
				}

				Console.WriteLine($"Video saved to: {videoFilePath}");
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

		public void ClipVideo(double startTimeInSeconds, double endTimeInSeconds, string timestampDir)
		{
			if (string.IsNullOrEmpty(videoFilePath) || !File.Exists(videoFilePath))
			{
				Console.WriteLine("No video file found to clip.");
				return;
			}

			string outputFilePath = GenerateOutputFileName(timestampDir, "clip", "mp4");
			string clipCommand = $"{GetFFmpegPath()} -i \"{videoFilePath}\" -ss {startTimeInSeconds} -to {endTimeInSeconds} -c:v copy -c:a copy \"{outputFilePath}\"";

			Console.WriteLine($"Clipping video from {startTimeInSeconds} to {endTimeInSeconds} seconds...");
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
					Console.WriteLine($"Clipped video saved to: {outputFilePath}");
				}
			}
		}

		public void MergeAudioAndVideo(string audioFilePath, string outputDir)
		{
			if (string.IsNullOrEmpty(videoFilePath))
			{
				Console.WriteLine("No video file found to merge.");
				return;
			}

			if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
			{
				Console.WriteLine("Audio file is missing or invalid.");
				return;
			}

			// Generate the merged file path
			string outputFilePath = GenerateOutputFileName(outputDir, "merged", "mp4");

			// FFmpeg command to merge video and audio
			string mergeCommand = $"{GetFFmpegPath()} -i \"{videoFilePath}\" -i \"{audioFilePath}\" -c:v copy -c:a aac \"{outputFilePath}\"";

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
					Console.WriteLine($"Merged file saved to: {outputFilePath}");
				}
			}
		}

		public string GetRecordingTime()
		{
			if (startTime.HasValue)
			{
				TimeSpan elapsed = DateTime.Now - startTime.Value;
				return elapsed.ToString(@"hh\:mm\:ss");
			}
			return "Not recording";
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

		private string GenerateOutputFileName(string directory, string prefix, string extension)
			=> Path.Combine(directory, $"{prefix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}");

		private string GetFFmpegCommand(string outputFile)
			=> $"{GetFFmpegPath()} -hwaccel cuda -loglevel error -nostats -hide_banner -f gdigrab " +
			   $"-framerate 60 -video_size 1920x1080 -offset_x 0 -offset_y 0 -rtbufsize 100M " +
			   $"-i desktop -c:v h264_nvenc -preset p1 -pix_fmt yuv420p -rc:v vbr_hq -cq:v 21 " +
			   $"-b:v 8M -maxrate:v 16M -bufsize:v 32M -vsync cfr -f mp4 \"{outputFile}\"";

		private string GetFFmpegPath()
		{
			// Adjust this path if FFmpeg is installed in a custom location
			return "ffmpeg";
		}

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
	}
}