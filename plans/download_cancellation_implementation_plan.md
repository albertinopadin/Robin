# Download Cancellation Implementation Plan for Robin

## Executive Summary
This plan outlines the implementation of download cancellation functionality for the Robin YouTube video downloader. 
The implementation will allow users to cancel in-progress downloads through a cancel button in the UI, 
properly handling cleanup and resource disposal.

## Current State Analysis

### Existing Infrastructure
- Async/await pattern already in use for downloads
- Progress reporting via `IProgress<double>` already implemented
- UI thread marshaling with `Control.Invoke()` in place
- ListView with embedded ProgressBar controls for each download
- TODOs already in code indicating planned cancellation feature

### Key Components to Modify
1. **YouTubeExplodeVideoDownloader** - Add CancellationToken support
2. **RobinForm** - Add cancel button UI and cancellation management
3. **YouTubeVideoDownloader** interface - Update method signatures

## Implementation Steps

### Step 1: Update Interface and Data Structures
**Files to modify:** `YouTubeVideoDownloader.cs`, `RobinForm.cs`

1. Add a new class to manage download state:
   ```csharp
   public class DownloadState
   {
       public CancellationTokenSource CancellationTokenSource { get; set; }
       public Task DownloadTask { get; set; }
       public string VideoUrl { get; set; }
       public ListViewItem ListViewItem { get; set; }
   }
   ```

2. Add Dictionary to RobinForm to track active downloads:
   ```csharp
   private Dictionary<string, DownloadState> activeDownloads = new Dictionary<string, DownloadState>();
   ```

### Step 2: Add CancellationToken Support to Download Methods
**Files to modify:** `YouTubeExplodeVideoDownloader.cs`

1. Update method signatures to accept CancellationToken:
   - `DownloadVideo()` method
   - `DownloadBestVideo()` methods
   - `DownloadVideo_Explode()` methods
   - `DownloadVideoAsync_Explode()` method

2. Pass CancellationToken to YoutubeExplode's DownloadAsync:
   ```csharp
   await youtube.Videos.DownloadAsync(videoId, 
                                      videoPath, 
                                      converter => converter.SetFFmpegPath(this.ffmpegPath), 
                                      progress,
                                      cancellationToken);
   ```

3. Add try-catch blocks to handle OperationCanceledException:
   ```csharp
   try
   {
       // Download code
   }
   catch (OperationCanceledException)
   {
       // Cleanup cancelled download
       if (File.Exists(videoPath))
           File.Delete(videoPath);
       throw; // Re-throw to notify caller
   }
   ```

### Step 3: Add Cancel Button to ListView
**Files to modify:** `RobinForm.cs`, `RobinForm.Designer.cs`

1. Modify `AddVideoItemToDownloadsList()` to add cancel button:
   ```csharp
   private void AddCancelButton(Rectangle bounds, string videoTitle)
   {
       Button cancelButton = new Button();
       cancelButton.Text = "Cancel";
       cancelButton.SetBounds(bounds.X, bounds.Y, 60, bounds.Height);
       cancelButton.Name = "cancel_" + videoTitle;
       cancelButton.Click += (s, e) => CancelDownload(videoTitle);
       listView_downloads.Controls.Add(cancelButton);
   }
   ```

2. Add new column to ListView for cancel button in Designer

3. Implement `CancelDownload()` method:
   ```csharp
   private void CancelDownload(string videoTitle)
   {
       if (activeDownloads.TryGetValue(videoTitle, out DownloadState state))
       {
           state.CancellationTokenSource.Cancel();
           // Update UI to show cancelled status
       }
   }
   ```

### Step 4: Update Download Flow
**Files to modify:** `RobinForm.cs`, `YouTubeExplodeVideoDownloader.cs`

1. Create CancellationTokenSource when starting download
2. Store in activeDownloads dictionary
3. Pass token through entire download chain
4. Remove from dictionary when download completes or cancels
5. Dispose CancellationTokenSource properly

### Step 5: Handle UI State for Cancelled Downloads
**Files to modify:** `RobinForm.cs`

1. Add new status: `videoStatusCancelled = "Cancelled"`
2. Update `NotifyDownloadFinished()` to handle cancelled state
3. Hide/disable cancel button when download completes or is cancelled
4. Update progress bar appearance for cancelled downloads

### Step 6: Error Handling and Edge Cases
**Files to modify:** All modified files

1. Handle partial file cleanup
2. Ensure CancellationTokenSource disposal
3. Handle rapid cancel/restart scenarios
4. Update existing duplicate download check logic

### Step 7: Testing Scenarios

1. **Basic cancellation**: Start download, cancel midway
2. **Multiple downloads**: Cancel one while others continue
3. **Rapid operations**: Start, cancel, restart same video
4. **Edge cases**: Cancel at 99%, cancel immediately after start
5. **Resource cleanup**: Verify no memory leaks, file handles released

## Implementation Order

1. **Phase 1 - Backend Support** (Steps 1-2)
   - Add data structures
   - Update method signatures
   - Add CancellationToken plumbing

2. **Phase 2 - UI Integration** (Steps 3-5)
   - Add cancel button
   - Wire up event handlers
   - Update UI states

3. **Phase 3 - Polish & Testing** (Steps 6-7)
   - Error handling
   - Edge cases
   - Comprehensive testing

## Risk Mitigation

### Risk 1: YoutubeExplode May Not Support CancellationToken
**Mitigation**: 
- First verify API support by checking method signatures
- If not supported, implement wrapper with periodic checks
- Consider using Task.Run with cancellation wrapper

### Risk 2: UI Thread Deadlocks
**Mitigation**:
- Use ConfigureAwait(false) where appropriate
- Ensure proper async/await usage
- Test thoroughly with multiple simultaneous downloads

### Risk 3: Resource Leaks
**Mitigation**:
- Implement proper IDisposable pattern
- Use using statements for CancellationTokenSource
- Add finally blocks for cleanup

## Success Criteria

1. ✅ User can cancel any in-progress download
2. ✅ Cancelled downloads show appropriate status
3. ✅ Partial files are cleaned up
4. ✅ No resource leaks or crashes
5. ✅ Other downloads continue unaffected
6. ✅ Can restart cancelled download

## Notes for Implementation

- Keep existing progress reporting intact
- Maintain thread safety with UI updates
- Consider adding "Pause" functionality in future iteration
- Log cancellation events for debugging
- Consider adding confirmation dialog for cancellation

## Estimated Effort

- Backend changes: 2-3 hours
- UI integration: 2-3 hours  
- Testing & refinement: 1-2 hours
- **Total: 5-8 hours**

## Alternative Approaches Considered

1. **Context Menu Instead of Button**: Right-click to cancel
   - Pros: Cleaner UI, less cluttered
   - Cons: Less discoverable for users

2. **Global Cancel All Button**: Single button to cancel all downloads
   - Pros: Simpler implementation
   - Cons: Less granular control

3. **Keyboard Shortcut**: Select + Delete key to cancel
   - Pros: Power user friendly
   - Cons: Not discoverable, accidental cancellation risk

**Recommendation**: Proceed with embedded cancel button approach for best user experience and discoverability.