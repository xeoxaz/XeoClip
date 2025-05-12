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
		private static readonly string[] options = { "Start Recording", "Stop Recording", "Exit" };
		private static string currentTimestampDir = string.Empty;
		private static readonly string baseDirectory = @"C:\test"; // Base directory for recordings and icons

		static void Main()
		{
			// Ensure base directory exists
			SetupBaseDirectory();

			// Initialize managers
			ffmpegManager = new FFmpegManager(baseDirectory);
			audioManager = new AudioManager();

			// Gracefully stop all processes on program exit
			AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();

			// Main menu loop
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
				Console.WriteLine($"Created base directory: {baseDirectory}");
			}

			// Ensure the recordings folder exists
			string recordingsFolder = Path.Combine(baseDirectory, "recordings");
			if (!Directory.Exists(recordingsFolder))
			{
				Directory.CreateDirectory(recordingsFolder);
				Console.WriteLine($"Created recordings directory: {recordingsFolder}");
			}
		}

		private static void CreateTimestampDirectory()
		{
			currentTimestampDir = Path.Combine(baseDirectory, "recordings", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
			Directory.CreateDirectory(currentTimestampDir);
			Console.WriteLine($"Created timestamp directory: {currentTimestampDir}");
		}

		private static void RenderMenu()
		{
			Console.Clear();
			Console.WriteLine("=== XeoClip Recorder ===");
			Console.WriteLine("Use ↑ ↓ arrows to navigate. Press Enter to select:");
			Console.WriteLine($"Recording Time: {ffmpegManager?.GetRecordingTime() ?? "Not recording"}");
			Console.WriteLine(new string('-', 30));

			for (int i = 0; i < options.Length; i++)
			{
				Console.BackgroundColor = i == selectedIndex ? ConsoleColor.White : ConsoleColor.Black;
				Console.ForegroundColor = i == selectedIndex ? ConsoleColor.Black : ConsoleColor.Gray;
				Console.WriteLine($"{(i == selectedIndex ? "> " : "  ")}{options[i]}");
				Console.ResetColor();
			}

			Console.WriteLine(new string('-', 30));
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
					Console.WriteLine("Invalid input. Use ↑ ↓ to navigate and Enter to select.");
					Thread.Sleep(1000); // Pause to let the user read the message
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

				case "Exit":
					ConfirmAndExit();
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
				Console.WriteLine("Recording is already in progress. Stop the current recording before starting a new one.");
				Thread.Sleep(2000); // Pause to let the user read the message
				return;
			}

			// Create timestamped directory for recordings
			CreateTimestampDirectory();

			// Start recording video and audio
			ffmpegManager.StartRecording(currentTimestampDir);
			audioManager.StartRecording(currentTimestampDir);

			// Initialize and start the icon watcher
			iconWatcher = new IconWatcher(baseDirectory, ffmpegManager);
			iconWatcher.StartWatching();

			Console.WriteLine("Recording started.");
			Thread.Sleep(1000); // Pause to let the user read the message
		}

		private static void StopRecording()
		{
			if (string.IsNullOrEmpty(currentTimestampDir))
			{
				Console.WriteLine("No recording is in progress to stop.");
				Thread.Sleep(2000); // Pause to let the user read the message
				return;
			}

			// Stop all active processes
			ffmpegManager?.StopRecording();
			audioManager?.StopRecording();
			iconWatcher?.StopWatching();

			// Merge video and audio into the BASE > RECORDINGS folder
			string recordingsFolder = Path.Combine(baseDirectory, "recordings");
			ffmpegManager?.MergeAudioAndVideo(audioManager?.AudioFilePath ?? string.Empty, recordingsFolder);

			Console.WriteLine("Recording stopped.");
			currentTimestampDir = string.Empty; // Reset directory path
			Thread.Sleep(1000); // Pause to let the user read the message
		}

		private static void ConfirmAndExit()
		{
			Console.WriteLine("Are you sure you want to exit? (Y/N)");
			var key = Console.ReadKey(true).Key;

			if (key == ConsoleKey.Y)
			{
				Console.WriteLine("Exiting program...");
				StopAll();
				Environment.Exit(0);
			}
			else
			{
				Console.WriteLine("Exit cancelled.");
				Thread.Sleep(1000); // Pause to let the user read the message
			}
		}

		private static void StopAll()
		{
			try
			{
				ffmpegManager?.StopRecording();
				audioManager?.StopRecording();
				iconWatcher?.StopWatching();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error during cleanup: {ex.Message}");
			}
		}
	}
}