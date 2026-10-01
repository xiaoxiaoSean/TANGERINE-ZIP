# UserChoiceLatest hash component

The hash codec and lookup tables are copied from
[cssxn/UserChoiceLatestHash](https://github.com/cssxn/UserChoiceLatestHash),
licensed under MIT. `TzipHashExport.cpp` is our narrow, read-only DLL adapter.
Build with `build.ps1` using Visual C++ x64 tools. The application embeds the
resulting `TzipLatestHash.dll` in its single-file publish output.
The checked-in x64 DLL SHA-256 is
`D7213215A1F1F65FCC511C74D7DE5360D4F3965B54FF5A565F030B8D40E82589`.

The upstream fixed seeds were verified against five existing UserChoiceLatest
associations on the development Windows 11 machine on 2026-10-01. This is not
a guarantee across Windows updates or machines. The app verifies an existing
hash with the same codec before attempting any default change, and checks the
effective Shell association afterward. The codec alone cannot bypass UCPD.
