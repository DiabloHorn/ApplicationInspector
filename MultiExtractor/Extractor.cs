﻿using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Archives.Rar;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using System;
using System.Collections.Generic;
using System.IO;

namespace MultiExtractor
{
    public static class Extractor
    {
        private const int BUFFER_SIZE = 4096;

        public static bool IsSupportedFormat(string filename)
        {
            ArchiveFileType archiveFileType = MiniMagic.DetectFileType(filename);
            return (archiveFileType != ArchiveFileType.UNKNOWN);
        }

        public static IEnumerable<FileEntry> ExtractFile(string filename)
        {
            try
            {
                if (!File.OpenRead(filename).CanRead)
                {
                    throw new IOException($"ExtractFile called, but {filename} cannot be read.");
                }
            }
            catch (Exception)
            {
                //Logger.Trace("File {0} cannot be read, ignoring.", filename);
                return Array.Empty<FileEntry>();
            }

            return ExtractFile(new FileEntry(filename, "", new MemoryStream(File.ReadAllBytes(filename))));
        }

        public static IEnumerable<FileEntry> ExtractFile(string filename, ArchiveFileType archiveFileType)
        {
            try
            {
                if (!File.OpenRead(filename).CanRead)
                {
                    throw new IOException($"ExtractFile called, but {filename} cannot be read.");
                }
            }
            catch (Exception)
            {
                //Logger.Trace("File {0} cannot be read, ignoring.", filename);
                return Array.Empty<FileEntry>();
            }

            using var memoryStream = new FileStream(filename, FileMode.Open);
            return ExtractFile(new FileEntry(filename, "", memoryStream), archiveFileType);
        }

        public static IEnumerable<FileEntry> ExtractFile(string filename, byte[] archiveBytes)
        {
            using var memoryStream = new MemoryStream(archiveBytes);
            return ExtractFile(new FileEntry(filename, "", memoryStream));
        }

        #region internal

        private static IEnumerable<FileEntry> ExtractFile(FileEntry fileEntry)
        {
            return ExtractFile(fileEntry, MiniMagic.DetectFileType(fileEntry));
        }

        private static IEnumerable<FileEntry> ExtractFile(FileEntry fileEntry, ArchiveFileType archiveFileType)
        {
            Console.WriteLine($"Extracting file {fileEntry.FullPath}");
            switch (archiveFileType)
            {
                case ArchiveFileType.ZIP:
                    return ExtractZipFile(fileEntry);

                case ArchiveFileType.GZIP:
                    return ExtractGZipFile(fileEntry);

                case ArchiveFileType.TAR:
                    return ExtractTarFile(fileEntry);

                case ArchiveFileType.XZ:
                    return ExtractXZFile(fileEntry);

                case ArchiveFileType.BZIP2:
                    return ExtractBZip2File(fileEntry);

                case ArchiveFileType.RAR:
                    return ExtractRarFile(fileEntry);

                case ArchiveFileType.P7ZIP:
                    return Extract7ZipFile(fileEntry);

                case ArchiveFileType.UNKNOWN:
                default:
                    return new[] { fileEntry };
            }
        }

        private static IEnumerable<FileEntry> ExtractZipFile(FileEntry fileEntry)
        {
            Console.WriteLine($"Extracting from Zip {fileEntry.FullPath}");
            //Console.WriteLine("Content Size => {0}", fileEntry.Content.Length);
            ZipFile zipFile = null;
            try
            {
                zipFile = new ZipFile(fileEntry.Content);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read from {fileEntry.FullPath} {e.Message} {e.StackTrace}");
            }
            if (zipFile != null)
            {
                foreach (ZipEntry zipEntry in zipFile)
                {
                    if (zipEntry.IsDirectory ||
                        zipEntry.IsCrypted ||
                        !zipEntry.CanDecompress)
                    {
                        continue;
                    }

                    using var memoryStream = new MemoryStream();
                    byte[] buffer = new byte[BUFFER_SIZE];
                    var zipStream = zipFile.GetInputStream(zipEntry);
                    StreamUtils.Copy(zipStream, memoryStream, buffer);

                    var newFileEntry = new FileEntry(zipEntry.Name, fileEntry.FullPath, memoryStream);
                    foreach (var extractedFile in ExtractFile(newFileEntry))
                    {
                        yield return extractedFile;
                    }
                }
            }
        }

        private static IEnumerable<FileEntry> ExtractGZipFile(FileEntry fileEntry)
        {
            using var gzipStream = new GZipInputStream(fileEntry.Content);
            using var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);

            var newFilename = Path.GetFileNameWithoutExtension(fileEntry.Name);
            if (fileEntry.Name.EndsWith(".tgz", System.StringComparison.CurrentCultureIgnoreCase))
            {
                if (newFilename.Length >= 3) //fix #191 short names e.g. a.tgz exception
                {
                    newFilename = newFilename[0..^4] + ".tar";
                }
                else
                {
                    newFilename += ".tar";
                }
            }

            var newFileEntry = new FileEntry(newFilename, fileEntry.FullPath, memoryStream);

            foreach (var extractedFile in ExtractFile(newFileEntry))
            {
                yield return extractedFile;
            }
        }

        private static IEnumerable<FileEntry> ExtractTarFile(FileEntry fileEntry)
        {
            TarEntry tarEntry;
            using var tarStream = new TarInputStream(fileEntry.Content);
            while ((tarEntry = tarStream.GetNextEntry()) != null)
            {
                if (tarEntry.IsDirectory)
                {
                    continue;
                }
                using var memoryStream = new MemoryStream();
                tarStream.CopyEntryContents(memoryStream);

                var newFileEntry = new FileEntry(tarEntry.Name, fileEntry.FullPath, memoryStream);
                foreach (var extractedFile in ExtractFile(newFileEntry))
                {
                    yield return extractedFile;
                }
            }
        }

        private static IEnumerable<FileEntry> ExtractXZFile(FileEntry fileEntry)
        {
            using var memoryStream = new MemoryStream();
            try
            {
                using var xzStream = new XZStream(fileEntry.Content);
                xzStream.CopyTo(memoryStream);
            }
            catch (Exception)
            {
                //
            }
            var newFilename = Path.GetFileNameWithoutExtension(fileEntry.Name);
            var newFileEntry = new FileEntry(newFilename, fileEntry.FullPath, memoryStream);
            foreach (var extractedFile in ExtractFile(newFileEntry))
            {
                yield return extractedFile;
            }
        }

        private static IEnumerable<FileEntry> ExtractBZip2File(FileEntry fileEntry)
        {
            List<FileEntry> files = new List<FileEntry>();
            using var bzip2Stream = new BZip2Stream(fileEntry.Content, SharpCompress.Compressors.CompressionMode.Decompress, false);
            using var memoryStream = new MemoryStream();
            bzip2Stream.CopyTo(memoryStream);

            var newFilename = Path.GetFileNameWithoutExtension(fileEntry.Name);
            var newFileEntry = new FileEntry(newFilename, fileEntry.FullPath, memoryStream);
            foreach (var extractedFile in ExtractFile(newFileEntry))
            {
                yield return extractedFile;
            }
        }

        private static IEnumerable<FileEntry> ExtractRarFile(FileEntry fileEntry)
        {
            List<FileEntry> files = new List<FileEntry>();
            using var rarArchive = RarArchive.Open(fileEntry.Content);

            foreach (var entry in rarArchive.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                var newFileEntry = new FileEntry(entry.Key, fileEntry.FullPath, entry.OpenEntryStream());
                foreach (var extractedFile in ExtractFile(newFileEntry))
                {
                    yield return extractedFile;
                }
            }
        }

        private static IEnumerable<FileEntry> Extract7ZipFile(FileEntry fileEntry)
        {
            List<FileEntry> files = new List<FileEntry>();
            using var rarArchive = RarArchive.Open(fileEntry.Content);

            foreach (var entry in rarArchive.Entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }
                var newFileEntry = new FileEntry(entry.Key, fileEntry.FullPath, entry.OpenEntryStream());
                foreach (var extractedFile in ExtractFile(newFileEntry))
                {
                    yield return extractedFile;
                }
            }
        }
    }

    #endregion internal
}