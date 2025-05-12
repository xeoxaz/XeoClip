using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAudio.Wave;
using OpenCvSharp;

namespace XeoClip
{
	internal class Program
	{
		private static FFmpegManager? ffmpegManager;
		private static AudioManager? audioManager;
		private static IconWatcher? iconWatcher;
		private static int selectedIndex = 0;
		private static readonly string[] options = { "Start Recording", "Stop Recording", "Clean Up", "Exit" };
		private static string currentTimestampDir = string.Empty;
		private static string? sharedTimestamp = null;
		private static string? audioFilePath = null;
		private static readonly string baseDirectory = @"C:\test"; // Base directory for recordings and icons

		// Automatically set debug based on the build configuration
		private static readonly bool debug =
#if DEBUG
			true;
#else
			false;
#endif

		static void Main()
		{
			SetupBaseDirectory();

			ffmpegManager = new FFmpegManager(baseDirectory);
			audioManager = new AudioManager();

			AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();

			while (true)
			{
				try
				{
					RenderMenu();
					ProcessInput();
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error: {ex.Message}");
					Thread.Sleep(2000); // Pause to let the user read the error
				}
			}
		}

		private static void SetupBaseDirectory()
		{
			if (!Directory.Exists(baseDirectory))
			{
				Directory.CreateDirectory(baseDirectory);
				Log($"Created base directory: {baseDirectory}");
			}

			string recordingsFolder = Path.Combine(baseDirectory, "recordings");
			if (!Directory.Exists(recordingsFolder))
			{
				Directory.CreateDirectory(recordingsFolder);
				Log($"Created recordings directory: {recordingsFolder}");
			}
		}

		private static void CreateTimestampDirectory()
		{
			currentTimestampDir = Path.Combine(baseDirectory, "recordings", sharedTimestamp ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
			Directory.CreateDirectory(currentTimestampDir);
			Log($"Created timestamp directory: {currentTimestampDir}");
		}

		private static void RenderMenu()
		{
			if (!debug)
				Console.Clear();

			// Header
			Console.BackgroundColor = ConsoleColor.DarkBlue;
			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine(" XeoClip Recorder ".PadRight(Console.WindowWidth - 1));
			Console.ResetColor();

			// Recording Time
			Console.WriteLine();
			var recordingTime = ffmpegManager?.GetRecordingTime();
			Console.WriteLine($"Recording Time: {(string.IsNullOrEmpty(recordingTime) ? "Not recording" : recordingTime)}");
			Console.WriteLine();

			// Menu Options
			for (int i = 0; i < options.Length; i++)
			{
				if (i == selectedIndex)
				{
					// Highlight selected option
					Console.BackgroundColor = ConsoleColor.DarkGray;
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"> {options[i]}".PadRight(Console.WindowWidth - 1));
					Console.ResetColor();
				}
				else
				{
					// Non-selected option
					Console.WriteLine($"  {options[i]}");
				}
			}

			// Navigation Instructions
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Blue;
			Console.WriteLine("Use ↑ ↓ arrows to navigate. Press Enter to select.");
			Console.ResetColor();
		}

		private static void ProcessInput()
		{
			switch (Console.ReadKey(true).Key)
			{
				case ConsoleKey.UpArrow:
					selectedIndex = Math.Max(selectedIndex - 1, 0);
					break;
				case ConsoleKey.DownArrow:
					selectedIndex = Math.Min(selectedIndex + 1, options.Length - 1);
					break;
				case ConsoleKey.Enter:
					HandleSelection();
					break;
				default:
					Log("Invalid input. Use ↑ ↓ to navigate and Enter to select.");
					Thread.Sleep(1000);
					break;
			}
		}

		private static void HandleSelection()
		{
			switch (options[selectedIndex])
			{
				case "Start Recording":
					StartRecording();
					break;
				case "Stop Recording":
					StopRecording();
					break;
				case "Clean Up":
					CleanUp();
					break;
				case "Exit":
					ExitProgram();
					break;
			}
		}

		private static void StartRecording()
		{
			if (ffmpegManager == null || audioManager == null)
			{
				throw new InvalidOperationException("Managers are not initialized.");
			}

			if (!string.IsNullOrEmpty(currentTimestampDir))
			{
				Log("Recording is already in progress. Stop the current recording before starting a new one.");
				Thread.Sleep(2000);
				return;
			}

			// Generate a shared timestamp
			sharedTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

			// Create a shared timestamp directory
			CreateTimestampDirectory();

			// Start video and audio recording with the same shared timestamp
			ffmpegManager.StartRecording(sharedTimestamp);
			audioManager.StartRecording(currentTimestampDir, sharedTimestamp);

			// Pass the audio file path to the global variable
			audioFilePath = Path.Combine(currentTimestampDir, $"audio_{sharedTimestamp}.wav");

			iconWatcher = new IconWatcher(baseDirectory, ffmpegManager);
			iconWatcher.StartWatching();

			Log("Recording started.");
			Thread.Sleep(1000);
		}

		private static void StopRecording()
		{
			if (string.IsNullOrEmpty(currentTimestampDir))
			{
				Log("No recording is in progress to stop.");
				Thread.Sleep(2000);
				return;
			}

			// Stop recording and merge audio-video
			var clipTimestamps = iconWatcher?.GetBufferedTimestamps() ?? new List<DateTime>();
			ffmpegManager?.StopRecording(audioFilePath ?? string.Empty, sharedTimestamp ?? string.Empty, clipTimestamps);
			audioManager?.StopRecording();
			iconWatcher?.StopWatching();

			Log("Recording stopped.");
			currentTimestampDir = string.Empty;
			sharedTimestamp = null;
			Thread.Sleep(1000);
		}

		private static void CleanUp()
		{
			string recordingsFolder = Path.Combine(baseDirectory, "recordings");
			if (Directory.Exists(recordingsFolder))
			{
				Directory.Delete(recordingsFolder, true);
				Directory.CreateDirectory(recordingsFolder);
				Log("All recordings have been cleaned up.");
			}
			else
			{
				Log("No recordings folder to clean up.");
			}
			Thread.Sleep(2000);
		}

		private static void ExitProgram()
		{
			Log("Exiting program...");
			StopAll();
			Environment.Exit(0);
		}

		private static void StopAll()
		{
			try
			{
				var clipTimestamps = iconWatcher?.GetBufferedTimestamps() ?? new List<DateTime>();
				ffmpegManager?.StopRecording(audioFilePath ?? string.Empty, sharedTimestamp ?? string.Empty, clipTimestamps);
				audioManager?.StopRecording();
				iconWatcher?.StopWatching();
			}
			catch (Exception ex)
			{
				Log($"Error during cleanup: {ex.Message}");
			}
		}

		private static void Log(string message)
		{
			if (debug)
			{
				Console.WriteLine($"[DEBUG] {message}");
			}
			else
			{
				Console.WriteLine(message);
			}
		}
	}
}