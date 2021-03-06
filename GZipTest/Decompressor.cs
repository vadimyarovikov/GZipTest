﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace GZipTest
{
    public class Decompressor : GZipWorker, IDecompressor
    {
        public void ParallelDecompress(FileInfo fileToDecompress, FileInfo decompressedFile, int numberOfWorkers)
        {
            if (!fileToDecompress.Exists)
            {
                Console.WriteLine("Source file does not exist. Please enter a valid file name.");
                return;
            }

            this.Read(fileToDecompress);

            Thread[] workers = new Thread[numberOfWorkers];
            ManualResetEvent resetEvent = new ManualResetEvent(false);
            int toProcess = numberOfWorkers;
            // Create and start a separate thread for each worker
            for (var i = 0; i < numberOfWorkers; i++)
            {
                (workers[i] = new Thread(
                     () =>
                         {
                             this.DecompressChunk();
                             if (Interlocked.Decrement(ref toProcess) == 0)
                                 resetEvent.Set();
                         })).Start();
            }

            // This is how to close output queue for writing file.
            // Othewise we somethimes get null queued before real data ends
            // And in that case not all file was written to disk.
            resetEvent.WaitOne();
            this.outputQueue.EnqueueNull();

            var writeThread = new Thread(new ParameterizedThreadStart(this.Write));
            writeThread.Start(decompressedFile);
        }

        protected override void Read(FileInfo inputFile)
        {
            var formatter = new BinaryFormatter();
            using (FileStream inputStream = inputFile.OpenRead())
            {
                while (inputStream.Position < inputStream.Length)
                {
                    var chunk = (Chunk)formatter.Deserialize(inputStream);
                    this.inputQueue.Enqueue(chunk);
                }

                this.inputQueue.EnqueueNull();
            }
        }

        private void DecompressChunk()
        {
            Chunk inputChunk;

            while ((inputChunk = this.inputQueue.Dequeue()) != null)
            {
                using (var memoryStream = new MemoryStream(inputChunk.Data))
                {
                    using (var zipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                    {
                        byte[] buffer = new byte[BUFFER_SIZE];
                        var numRead = zipStream.Read(buffer, 0, buffer.Length);
                        byte[] data = new byte[numRead];
                        Buffer.BlockCopy(buffer, 0, data, 0, numRead);
                        var outputChunk = new Chunk(inputChunk.Id, data);
                        this.outputQueue.Enqueue(outputChunk);
                    }
                }
            }
        }

        protected override void Write(object outputFileName)
        {
            var outputFile = (FileInfo)outputFileName;
            using (FileStream outFile = File.Create(outputFile.FullName))
            {
                Chunk chunk;
                while ((chunk = this.outputQueue.Dequeue()) != null)
                {
                    outFile.Write(chunk.Data, 0, chunk.Data.Length);
                }
            }

            Console.WriteLine("File {0} created with {1} bytes length.", outputFile.Name, outputFile.Length);
            Console.ReadLine();
        }

        #region SingleThreaded

        public void Decompress(FileInfo fileToDecompress, FileInfo decompressedFile)
        {
            using (FileStream inputStream = fileToDecompress.OpenRead())
            {
                using (FileStream outFile = File.Create(decompressedFile.FullName))
                {
                    using (var gZipStream = new GZipStream(outFile, CompressionMode.Decompress))
                    {
                        byte[] buffer = new byte[BUFFER_SIZE];
                        int numRead;
                        while ((numRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            gZipStream.Write(buffer, 0, numRead);
                        }

                        Console.WriteLine(
                            "Decompressed {0} from {1} to {2} bytes.",
                            fileToDecompress.Name,
                            fileToDecompress.Length,
                            outFile.Length);
                    }
                }
            }
        }

        #endregion
    }
}