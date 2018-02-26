﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GZipTest
{
    public class Compressor : GZipWorker, ICompressor
    {
        public void Compress(FileInfo fileToCompress)
        {
            this.Compress(fileToCompress, new FileInfo(fileToCompress.FullName + ".gz"));
        }

        public void Compress(FileInfo fileToCompress, FileInfo compressedFile)
        {
            using (FileStream inputStream = fileToCompress.OpenRead())
            {
                using (FileStream outFile = File.Create(compressedFile.FullName))
                {
                    using (GZipStream gZipStream = new GZipStream(outFile, CompressionMode.Compress))
                    {
                        byte[] buffer = new byte[BUFFER_SIZE];
                        int numRead;
                        while ((numRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            gZipStream.Write(buffer, 0, numRead);
                        }

                        Console.WriteLine(
                            "Compressed {0} from {1} to {2} bytes.",
                            fileToCompress.Name,
                            fileToCompress.Length,
                            outFile.Length);
                    }
                }
            }
        }

        private ChunkQueue inputQueue = new ChunkQueue();
        private ChunkQueue outputQueue = new ChunkQueue();

        public void ParallelCompress(FileInfo fileToCompress, FileInfo compressedFile, int numberOfWorkers)
        {
            Thread[] workers = new Thread[numberOfWorkers];
            // Create and start a separate thread for each worker
            for (var i = 0; i < numberOfWorkers; i++)
            {
                (workers[i] = new Thread(this.CompressChunk)).Start();
            }

            this.Read(fileToCompress);

            byte[] buffer = new byte[BUFFER_SIZE];
            using (FileStream inputStream = fileToCompress.OpenRead())
            {
                // Producer-consumers
                // Producer reads file by chunks and saves them to queue.
                // Consumers take chunsk from queue and perform compression
                var result = new List<Chunk>();

                while (inputStream.Read(buffer, 0, buffer.Length) > 0)
                {
                    //chunkProducerConsumer.Enqueue(buffer);
                    buffer = new byte[BUFFER_SIZE];
                }

                WriteOutputFile(compressedFile, result);
            }
        }

        private void Read(FileInfo inputFile)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            using (FileStream inputStream = inputFile.OpenRead())
            {
                int numRead = 0;
                while ((numRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    byte[] byteChunk = new byte[numRead];
                    Buffer.BlockCopy(byteChunk, 0, byteChunk, 0, numRead);
                    this.inputQueue.Enqueue(byteChunk);
                }

                this.inputQueue.Finish();
            }
        }

        private void CompressChunk()
        {
            Chunk inputChunk;
            while ((inputChunk = this.inputQueue.Dequeue()) != null)
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var zipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                    using (var binaryWriter = new BinaryWriter(zipStream))
                    {
                        binaryWriter.Write(inputChunk.Data, 0, inputChunk.Data.Length);
                    }

                    byte[] outputChunkData = memoryStream.ToArray();
                    var outputChunk = new Chunk(inputChunk.Id, outputChunkData);
                    this.outputQueue.Enqueue(outputChunk);
                }
            }
        }

        private static void WriteOutputFile(FileInfo compressedFile, List<Chunk> result)
        {
            using (FileStream outFile = File.Create(compressedFile.FullName))
            {
                foreach (var chunk in result.OrderBy(x => x.Id))
                {
                    outFile.Write(chunk.Data, 0, chunk.Data.Length);
                }
            }
        }
    }
}