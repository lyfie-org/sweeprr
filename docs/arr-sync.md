# Radarr & Sonarr (Arr) Sync Logic

A major issue with media library cleanup tools is the "re-download loop": if you delete a file from disk, Radarr or Sonarr will notice it is missing during the next disk scan and immediately search for and download it again. 

Sweeprr breaks this loop by integrating directly with the Arr APIs to unmonitor content *before* performing any disk deletion.

## The 5-Step Execution Flow

When a sweep execution is approved (manually in the queue or scheduled automatically), the `SweepExecutor` executes the following sequence on the target Arr instance:

```
[1] Exclude Item (Import Exclusions) 
       └───► [2] Unmonitor Item in Radarr/Sonarr
                   └───► [3] Enforce Unmonitor Success
                               └───► [4] Delete Files via API
                                           └───► [5] Log & Verify
```

1. **Add Import List Exclusions**: Sweeprr can optionally add the movie or TV show to the Arr's import list exclusion table. This ensures it won't be re-added if you have automated list syncs (e.g. Trakt, IMDb lists) running.
2. **Unmonitor Content**: Send a `PUT` request to update the Arr's database record.
   - For **Movies**: Sets `monitored = false`.
   - For **Seasons/Episodes**: Flips the specific season or episode monitor flag to false.
3. **Verify Unmonitor**: Sweeprr verifies that the Arr successfully applied the unmonitored state.
4. **Delete Files**: Trigger a deletion request to the Arr's API with `deleteFiles=true`. Deleting via the Arr API is preferred over direct disk deletion because it updates the Arr database immediately and prevents indexer mismatch warnings.
5. **Validation**: Sweeprr verifies that the Arr reported a successful file deletion.

---

## Season vs. Series Deletion (Sonarr)

- **Season Sweep**: When a specific season is targeted for cleanup, Sonarr unmonitors only that season. The overall series remains monitored if other seasons are still monitored. Once the season files are deleted, the disk folder is cleaned up.
- **Series Sweep**: If the entire show is marked for deletion, Sonarr unmonitors the series, adds the exclusion, and deletes all files.

> [!WARNING]
> **Order of Operations**:
> Sweeprr enforces that unmonitoring **MUST** complete successfully before file deletion is triggered. If the unmonitor request fails (e.g., timeout or bad API credentials), the execution for that item is aborted, and its files are left untouched.
