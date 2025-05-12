using NAudio.Wave;
using System;
using System.IO;

namespace XeoClip
{
	internal class AudioManager
	{
		private WasapiLoopbackCapture? loopbackCapture;
		private MemoryStream? audioBuffer;
		private WaveFileWriter? waveFileWriter;
		public string? AudioFilePath { get; private set; }

		public void StartRecording(string timestampDir, string sharedTimestamp)
		{
			try
			{
				AudioFilePath = GenerateOutputFileName(timestampDir, "audio", "wav", sharedTimestamp);

				// Initialize WASAPI Loopback Capture
				loopbackCapture = new WasapiLoopbackCapture();

				// Initialize a memory buffer to store audio data temporarily
				audioBuffer = new MemoryStream();

				loopbackCapture.DataAvailable += (s, e) =>
				{
					if (e.BytesRecorded > 0)
					{
						// Write audio data to the memory buffer
						audioBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
					}
				};

				loopbackCapture.RecordingStopped += (s, e) =>
				{
					try
					{
						// Write the buffer contents to a file
						WriteBufferToFile();

						// Dispose of resources
						audioBuffer?.Dispose();
						audioBuffer = null;
						loopbackCapture?.Dispose();
						loopbackCapture = null;
					}
					catch
					{
						// Handle any exceptions silently in release builds
					}
				};

				loopbackCapture.StartRecording();
			}
			catch
			{
				// Handle any exceptions silently in release builds
			}
		}

		public void StopRecording()
		{
			if (loopbackCapture == null)
			{
				return;
			}

			try
			{
				loopbackCapture.StopRecording();
			}
			catch
			{
				// Handle any exceptions silently in release builds
			}
		}

		private void WriteBufferToFile()
		{
			if (audioBuffer == null || audioBuffer.Length == 0)
			{
				return; // No audio data to write
			}

			try
			{
				audioBuffer.Seek(0, SeekOrigin.Begin); // Reset the buffer position to the start

				// Create a WaveFileWriter to save the buffer as a WAV file
				using var waveWriter = new WaveFileWriter(AudioFilePath, loopbackCapture?.WaveFormat ?? new WaveFormat());
				audioBuffer.CopyTo(waveWriter);
			}
			catch
			{
				// Handle exceptions silently
			}
		}

		private string GenerateOutputFileName(string directory, string prefix, string extension, string sharedTimestamp)
			=> Path.Combine(directory, $"{prefix}_{sharedTimestamp}.{extension}");
	}
}