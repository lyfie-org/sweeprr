namespace Sweeprr.API.Models;

public enum UserRole { Admin }

public enum ConnectionType { Jellyfin, Radarr, Sonarr }

public enum MediaType { Movie, Series, Season, Episode }

public enum SweepAction
{
    DeleteAndUnmonitor,
    UnmonitorOnly,
    DeleteOnly,
    DeleteSeriesIfEmpty,
    UnmonitorSeasonIfEmpty,
    ChangeQualityProfile = 6,
}

public enum LogicalOperator { And, Or }

public enum RuleValueType { Number, Date, Text, Bool, RelativeDays, TextList }

public enum SweepItemStatus { Pending, Approved, Ignored, Swept, Failed }

public enum ActivityLogLevel { Debug, Information, Warning, Error }

public enum ActivityLogCategory { Sweep, Connection, Rule, System, Auth }
