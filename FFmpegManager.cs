using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading;

namespace XeoClip
{
	internal class FFmpegManager
	{
		private Process? ffmpegProcess;
		private DateTime? startTime;
		private string? videoFilePath;
		private string? mergedFilePath;
		private Thread? recordingThread;
		private readonly string baseDirectory;

		public FFmpegManager(string baseDirectory)
		{
			this.baseDirectory = baseDirectory;
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

			// Generate timestamped directory
			string timestampDir = GenerateTimestampedDirectory();

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

		public void StopRecording(string audioFilePath, List<DateTime> clipTimestamps)
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

			// Automatically merge audio and video
			if (!string.IsNullOrEmpty(audioFilePath) && File.Exists(audioFilePath))
			{
				var timestampDir = Path.GetDirectoryName(videoFilePath);
				MergeAudioAndVideo(audioFilePath, timestampDir);

				// Check for clip timestamps and create clips
				if (clipTimestamps != null && clipTimestamps.Count > 0)
				{
					CreateClipsFromTimestamps(clipTimestamps, timestampDir);
				}
			}
			else
			{
				Console.WriteLine("Audio file is missing or invalid. Skipping merge and clip creation.");
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
			mergedFilePath = GenerateOutputFileName(outputDir, "merged", "mp4");

			// FFmpeg command to merge video and audio
			string mergeCommand = $"{GetFFmpegPath()} -i \"{videoFilePath}\" -i \"{audioFilePath}\" -c:v copy -c:a aac \"{mergedFilePath}\"";

			Console.WriteLine("Merging video and audio...");
			RunFFmpeg(mergeCommand);

			if (File.Exists(mergedFilePath))
			{
				Console.WriteLine($"Merged file saved to: {mergedFilePath}");
			}
		}

		public void CreateClipsFromTimestamps(List<DateTime> timestamps, string outputDir)
		{
			if (string.IsNullOrEmpty(mergedFilePath) || !File.Exists(mergedFilePath))
			{
				Console.WriteLine("No merged file found to create clips.");
				return;
			}

			Console.WriteLine("Creating clips from timestamps...");
			for (int i = 0; i < timestamps.Count - 1; i++)
			{
				double startTime = (timestamps[i] - timestamps.First()).TotalSeconds;
				double endTime = (timestamps[i + 1] - timestamps.First()).TotalSeconds;

				string clipFilePath = GenerateOutputFileName(outputDir, $"clip_{i + 1}", "mp4");
				string clipCommand = $"{GetFFmpegPath()} -i \"{mergedFilePath}\" -ss {startTime} -to {endTime} -c:v copy -c:a copy \"{clipFilePath}\"";

				Console.WriteLine($"Creating clip {i + 1} from {startTime:F2}s to {endTime:F2}s...");
				RunFFmpeg(clipCommand);

				if (File.Exists(clipFilePath))
				{
					Console.WriteLine($"Clip {i + 1} saved to: {clipFilePath}");
				}
				else
				{
					Console.WriteLine($"Failed to create clip {i + 1}.");
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

		private string GenerateTimestampedDirectory()
		{
			string recordingsDir = Path.Combine(baseDirectory, "recordings");
			string timestampDir = Path.Combine(recordingsDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
			Directory.CreateDirectory(timestampDir);
			return timestampDir;
		}

		private string GenerateOutputFileName(string directory, string prefix, string extension)
		{
			string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			return Path.Combine(directory, $"{prefix}_{timestamp}.{extension}");
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