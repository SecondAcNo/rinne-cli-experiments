namespace Rinne.Core.Common;

public readonly record struct RinnePaths(string SourceRoot)
{
    public string RinneRoot => Path.Join(SourceRoot, ".rinne");

    public string RinneIgnoreJson => Path.Join(SourceRoot, "rinneignore.json");

    public string ConfigDir => Path.Join(RinneRoot, "config");
    public string ConfigJson => Path.Join(ConfigDir, "config.json");

    public string SnapshotsRoot => Path.Join(RinneRoot, "snapshots");
    public string SnapshotsCurrent => Path.Join(SnapshotsRoot, "current");
    public string SpacesRoot => Path.Join(SnapshotsRoot, "space");
    public string SnapshotsSpace(string space) => Path.Join(SpacesRoot, space);
    public string Snapshot(string space, string snapshotId) => Path.Join(SnapshotsSpace(space), snapshotId);

    public string SnapshotPayload(string space, string snapshotId)
        => Path.Join(Snapshot(space, snapshotId), "snapshots");

    public string StoreRoot => Path.Join(RinneRoot, "store");
    public string StoreManifests => Path.Join(StoreRoot, "manifests");
    public string StoreMeta => Path.Join(StoreRoot, ".meta");
    public string StoreTmp => Path.Join(StoreRoot, ".tmp");

    public string StoreManifest(string snapshotId) => Path.Join(StoreManifests, $"{snapshotId}.json");

    public string LogsDir => Path.Join(RinneRoot, "logs");
    public string TempDir => Path.Join(RinneRoot, "temp");
}
