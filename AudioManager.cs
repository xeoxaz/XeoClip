using NAudio.Wave;
using System;
using System.IO;

namespace XeoClip
{
	internal class AudioManager
	{
		private WasapiLoopbackCapture? loopbackCapture;
		private WaveFileWriter? waveFileWriter;
		public string? AudioFilePath { get; private set; }
		private long totalBytesWritten = 0; // Tracks the total bytes written to the audio file

		public void StartRecording(string timestampDir, string sharedTimestamp)
		{
			Console.WriteLine("Starting system audio recording...");
			AudioFilePath = GenerateOutputFileName(timestampDir, "audio", "wav", sharedTimestamp);

			// Initialize WASAPI Loopback Capture
			loopbackCapture = new WasapiLoopbackCapture();

			// Set up the WaveFileWriter to save audio data
			waveFileWriter = new WaveFileWriter(AudioFilePath, loopbackCapture.WaveFormat);

			loopbackCapture.DataAvailable += (s, e) =>
			{
				// Write audio data to file
				if (e.BytesRecorded > 0)
				{
					waveFileWriter?.Write(e.Buffer, 0, e.BytesRecorded);
					totalBytesWritten += e.BytesRecorded; // Track the number of bytes written
				}
			};

			loopbackCapture.RecordingStopped += (s, e) =>
			{
				waveFileWriter?.Dispose();
				waveFileWriter = null;
				loopbackCapture?.Dispose();
				loopbackCapture = null;

				// Check if any audio data was captured
				if (totalBytesWritten == 0)
				{
					Console.WriteLine("No audio captured. Generating silent audio...");
					GenerateSilentAudio(AudioFilePath, DateTime.Now - recordingStartTime);
					Console.WriteLine($"Silent audio file saved to: {AudioFilePath}");
				}
				else
				{
					Console.WriteLine($"Audio recording complete. File saved to: {AudioFilePath}");
				}
			};

			recordingStartTime = DateTime.Now; // Record the start time
			loopbackCapture.StartRecording();
		}

		public void StopRecording()
		{
			if (loopbackCapture == null)
			{
				Console.WriteLine("No active audio recording to stop.");
				return;
			}

			try
			{
				Console.WriteLine("Stopping system audio recording...");
				loopbackCapture.StopRecording();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error stopping audio recording: {ex.Message}");
			}
			finally
			{
				// Reset the total bytes written for the next recording
				totalBytesWritten = 0;
			}
		}

		private void GenerateSilentAudio(string filePath, TimeSpan duration)
		{
			// Create a silent audio file with the same format as the original recording
			using var waveFile = new WaveFileWriter(filePath, loopbackCapture?.WaveFormat ?? new WaveFormat());
			var silenceBuffer = new byte[waveFile.WaveFormat.AverageBytesPerSecond];

			for (int i = 0; i < duration.TotalSeconds; i++)
			{
				waveFile.Write(silenceBuffer, 0, silenceBuffer.Length);
			}
		}

		private string GenerateOutputFileName(string directory, string prefix, string extension, string sharedTimestamp)
			=> Path.Combine(directory, $"{prefix}_{sharedTimestamp}.{extension}");

		private DateTime recordingStartTime; // Tracks the start time of the recording
	}
}