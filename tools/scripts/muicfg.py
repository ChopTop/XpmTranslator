"""Print the MUI resource configuration (checksums, file type, language) of PE files."""
import struct, sys
import pefile

TYPES = {0x01: 'LN (neutral, has fallback)', 0x02: 'MUI satellite', 0x12: 'LN (no fallback?)', 0x11: 'MUI'}
for p in sys.argv[1:]:
    pe = pefile.PE(p, fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_RESOURCE']])
    blob = None
    for t in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if t.name is not None and str(t.name) == 'MUI':
            e = t.directory.entries[0].directory.entries[0].data.struct
            blob = pe.get_data(e.OffsetToData, e.Size)
    if blob is None:
        print(p, 'no MUI resource')
        continue
    sig, size, ver, path_type, file_type, sys_attr, ult_attr = struct.unpack_from('<IIIIIII', blob, 0)
    service_ck = blob[0x1C:0x2C].hex()
    ck = blob[0x2C:0x3C].hex()
    # language names are stored as UTF-16 at offsets given in the config
    fields = struct.unpack_from('<' + 'I' * 22, blob, 0x48)
    lang = ''
    for off, ln in ((fields[16], fields[17]), (fields[18], fields[19])):
        if off and ln:
            lang += blob[off:off + ln].decode('utf-16le', 'replace').strip('\x00') + ' '
    print(f'{p}\n   fileType=0x{file_type:x} ({TYPES.get(file_type, "?")}) svcCk={service_ck} ck={ck} lang={lang.strip()}')
