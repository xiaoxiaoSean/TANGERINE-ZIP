# SFTA attribution

Upstream: <https://github.com/DanysysTeam/SFTA>, `SFTA.pb` version 1.3.1.

Authors: Danyfirex & Dany3j (Danysys). Upstream credits LMongrain for the hash algorithm.
License: MIT; the unmodified upstream license is in [LICENSE](LICENSE).

`TANGERINE-ZIP/Services/SftaUserChoice.cs` ports `Hash1`, `Hash2`, `GenerateHash`,
`CreateProgIdHash` and the file association workflow to managed C#. It adds
existing-hash validation, minute-boundary handling, effective association checks,
current-user serialization and rollback with a regenerated hash. It does not ship
or execute SFTA.exe, PowerShell, or the upstream precompiled `Hash.a` archive.

Downloaded `SFTA.pb` SHA256:
`9572B50BC8C84FEF1558E047D80E3D63C1426A39C3828C3286B956BF64164C17`.

This uses undocumented Windows behavior. Upstream's advertised OS support does
not establish compatibility with every subsequent Windows update.
