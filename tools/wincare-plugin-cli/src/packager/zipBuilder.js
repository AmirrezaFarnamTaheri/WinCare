const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

/**
 * Generates a simple, standard uncompressed or deflated ZIP buffer deterministically.
 */
class DeterministicZipWriter {
  constructor() {
    this.files = [];
  }

  addFile(relativePath, data) {
    const cleanPath = relativePath.replace(/\\/g, '/').replace(/^\/+/, '');
    const buffer = Buffer.isBuffer(data) ? data : Buffer.from(data);
    this.files.push({ path: cleanPath, data: buffer });
  }

  // Calculate CRC-32
  _crc32(buf) {
    let crc = ~0;
    for (let i = 0; i < buf.length; i++) {
      crc = (crc >>> 8) ^ CRC_TABLE[(crc ^ buf[i]) & 0xFF];
    }
    return (~crc) >>> 0;
  }

  build() {
    const localHeaders = [];
    const centralDirectoryEntries = [];
    let offset = 0;

    // Sort entries deterministically by path
    this.files.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);

    for (const file of this.files) {
      const pathBuf = Buffer.from(file.path, 'utf8');
      const uncompressedSize = file.data.length;
      const crc = this._crc32(file.data);

      // Deflate
      const compressedData = zlib.deflateRawSync(file.data);
      const compressedSize = compressedData.length;

      // Local File Header (30 bytes + filename)
      const localHeader = Buffer.alloc(30 + pathBuf.length);
      localHeader.writeUInt32LE(0x04034b50, 0); // Signature
      localHeader.writeUInt16LE(20, 4);          // Version needed (2.0)
      localHeader.writeUInt16LE(0x0800, 6);      // UTF-8 filenames
      localHeader.writeUInt16LE(8, 8);           // Compression method (8 = Deflate)
      localHeader.writeUInt16LE(0x4000, 10);     // Fixed MS-DOS time (deterministic)
      localHeader.writeUInt16LE(0x5600, 12);     // Fixed MS-DOS date (deterministic)
      localHeader.writeUInt32LE(crc, 14);        // CRC-32
      localHeader.writeUInt32LE(compressedSize, 18); // Compressed size
      localHeader.writeUInt32LE(uncompressedSize, 22); // Uncompressed size
      localHeader.writeUInt16LE(pathBuf.length, 26); // File name length
      localHeader.writeUInt16LE(0, 28);          // Extra field length
      pathBuf.copy(localHeader, 30);

      localHeaders.push(localHeader, compressedData);

      // Central Directory Entry (46 bytes + filename)
      const cdEntry = Buffer.alloc(46 + pathBuf.length);
      cdEntry.writeUInt32LE(0x02014b50, 0); // Signature
      cdEntry.writeUInt16LE(20, 4);          // Version made by
      cdEntry.writeUInt16LE(20, 6);          // Version needed
      cdEntry.writeUInt16LE(0x0800, 8);      // UTF-8 filenames
      cdEntry.writeUInt16LE(8, 10);          // Compression (Deflate)
      cdEntry.writeUInt16LE(0x4000, 12);     // Fixed Time
      cdEntry.writeUInt16LE(0x5600, 14);     // Fixed Date
      cdEntry.writeUInt32LE(crc, 16);        // CRC-32
      cdEntry.writeUInt32LE(compressedSize, 20); // Compressed size
      cdEntry.writeUInt32LE(uncompressedSize, 24); // Uncompressed size
      cdEntry.writeUInt16LE(pathBuf.length, 28); // File name length
      cdEntry.writeUInt16LE(0, 30);          // Extra field length
      cdEntry.writeUInt16LE(0, 32);          // File comment length
      cdEntry.writeUInt16LE(0, 34);          // Disk number start
      cdEntry.writeUInt16LE(0, 36);          // Internal file attributes
      cdEntry.writeUInt32LE(0, 38);          // External file attributes
      cdEntry.writeUInt32LE(offset, 42);     // Relative offset of local header
      pathBuf.copy(cdEntry, 46);

      centralDirectoryEntries.push(cdEntry);

      offset += localHeader.length + compressedData.length;
    }

    const cdOffset = offset;
    let cdSize = 0;
    for (const cd of centralDirectoryEntries) {
      cdSize += cd.length;
    }

    // End of Central Directory Record (22 bytes)
    const eocd = Buffer.alloc(22);
    eocd.writeUInt32LE(0x06054b50, 0); // Signature
    eocd.writeUInt16LE(0, 4);          // Number of this disk
    eocd.writeUInt16LE(0, 6);          // Disk where central directory starts
    eocd.writeUInt16LE(this.files.length, 8); // Total entries on this disk
    eocd.writeUInt16LE(this.files.length, 10); // Total entries
    eocd.writeUInt32LE(cdSize, 12);    // Size of central directory
    eocd.writeUInt32LE(cdOffset, 16);  // Offset of start of central directory
    eocd.writeUInt16LE(0, 20);         // Comment length

    return Buffer.concat([...localHeaders, ...centralDirectoryEntries, eocd]);
  }
}

// CRC32 Lookup Table
const CRC_TABLE = new Uint32Array(256);
for (let i = 0; i < 256; i++) {
  let c = i;
  for (let k = 0; k < 8; k++) {
    c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
  }
  CRC_TABLE[i] = c >>> 0;
}

/**
 * Packs a plugin folder into a .wincare-plugin ZIP archive.
 * @param {string} pluginDir - Directory containing wincare-plugin.json
 * @param {string} outputPath - Output file path (.wincare-plugin)
 * @returns {{ success: boolean, outputPath: string, fileCount: number, sizeBytes: number }}
 */
function packPlugin(pluginDir, outputPath) {
  const resolvedDir = path.resolve(pluginDir);
  const targetOutput = path.resolve(outputPath || path.join(resolvedDir, 'plugin.wincare-plugin'));
  const zip = new DeterministicZipWriter();
  let totalBytes = 0;

  function scanDir(currentDir, baseDir) {
    const items = fs.readdirSync(currentDir);
    for (const item of items) {
      if (item === 'node_modules' || item.startsWith('.')) continue;
      const fullPath = path.join(currentDir, item);
      const stat = fs.lstatSync(fullPath);
      if (stat.isSymbolicLink()) {
        throw new Error(`Plugin packages cannot contain symbolic links: ${item}`);
      }
      if (stat.isDirectory()) {
        scanDir(fullPath, baseDir);
      } else if (stat.isFile()) {
        const relativePath = path.relative(baseDir, fullPath);
        if (path.resolve(fullPath) === targetOutput) continue;
        totalBytes += stat.size;
        if (zip.files.length >= 500 || totalBytes > 200 * 1024 * 1024) {
          throw new Error('Plugin package exceeds the installer entry or uncompressed size limit.');
        }
        const data = fs.readFileSync(fullPath);
        zip.addFile(relativePath, data);
      }
    }
  }

  scanDir(resolvedDir, resolvedDir);

  const archiveBuffer = zip.build();
  if (archiveBuffer.length > 50 * 1024 * 1024) {
    throw new Error('Plugin package exceeds the 50 MiB installer archive limit.');
  }
  fs.writeFileSync(targetOutput, archiveBuffer);

  return {
    success: true,
    outputPath: targetOutput,
    fileCount: zip.files.length,
    sizeBytes: archiveBuffer.length
  };
}

module.exports = {
  DeterministicZipWriter,
  packPlugin
};
