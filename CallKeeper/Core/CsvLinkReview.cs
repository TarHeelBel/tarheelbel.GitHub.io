namespace MeetingDirectory;

public sealed record CsvLinkChange(Meeting Existing, Meeting Incoming) {
    public Meeting Apply() => Existing with {
        Link = Incoming.Link,
        MeetingId = Incoming.MeetingId,
        Passcode = Incoming.Passcode
    };
}

public sealed record CsvLinkReview(List<Meeting> Additions, List<CsvLinkChange> Changes, List<string> Ambiguous) {
    public static CsvLinkReview Plan(IEnumerable<Meeting> existing, IEnumerable<Meeting> selected) {
        var byIdentity = existing.ToLookup(Identity);
        var additions = new List<Meeting>();
        var changes = new List<CsvLinkChange>();
        var ambiguous = new List<string>();
        foreach (var group in selected.GroupBy(Identity)) {
            var matches = byIdentity[group.Key].ToArray();
            var rows = group.ToArray();
            foreach (var row in rows) {
                if (matches.Length == 0 || matches.Any(m => m.Link == row.Link)) {
                    additions.Add(row);
                } else if (matches.Length == 1 && rows.Length == 1) {
                    changes.Add(new CsvLinkChange(matches[0], row));
                } else {
                    var account = row.ZoomAccount.Length > 0 ? row.ZoomAccount : "No account label";
                    ambiguous.Add($"{row.Name} ({row.Platform}, {account})");
                }
            }
        }
        return new CsvLinkReview(additions, changes, ambiguous);
    }

    static (string Name, string Platform, string Account) Identity(Meeting m) =>
        (m.Name.Trim().ToUpperInvariant(), m.Platform, m.ZoomAccount.Trim().ToUpperInvariant());
}
