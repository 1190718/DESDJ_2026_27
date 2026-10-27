using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    [SerializeField] private Checkpoint[] checkpoints;

    private int currentCheckpointIndex = 0;

    private void Start()
    {
        for (int i = 0; i < checkpoints.Length; i++)
        {
            // The final checkpoint starts hidden
            if (i == checkpoints.Length - 1)
            {
                checkpoints[i].gameObject.SetActive(false);
            }
            else
            {
                // Only the first checkpoint starts active
                checkpoints[i].Initialize(this, i == 0);
            }
        }
    }

    public void CheckpointCompleted(Checkpoint completedCheckpoint)
    {
        // Make sure the completed checkpoint is the one we're expecting
        if (currentCheckpointIndex >= checkpoints.Length)
            return;

        if (completedCheckpoint != checkpoints[currentCheckpointIndex])
            return;

        // If this checkpoint belongs to an island with a hidden rule, the island must be
        // cleared (guardian defeated) before the next checkpoint unlocks.
        MistIsles.World.IslandController island =
            completedCheckpoint.GetComponentInParent<MistIsles.World.IslandController>();
        if (island != null)
            island.RuleSolved(AdvanceToNextCheckpoint);
        else
            AdvanceToNextCheckpoint();
    }

    private void AdvanceToNextCheckpoint()
    {
        // Move to the next checkpoint
        currentCheckpointIndex++;

        // If there is another checkpoint
        if (currentCheckpointIndex < checkpoints.Length)
        {
            Checkpoint nextCheckpoint = checkpoints[currentCheckpointIndex];
            // The final checkpoint starts hidden, so show it when it becomes the target.
            nextCheckpoint.gameObject.SetActive(true);
            nextCheckpoint.Initialize(this, true);
        }
    }
}