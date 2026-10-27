using UnityEngine;

namespace MistIsles.Rules
{
    /// <summary>
    /// Still Shore: creatures may only move while the player moves.
    /// When the player stands still, every crab (and the guardian) freezes mid-action.
    /// </summary>
    [CreateAssetMenu(fileName = "StillShore_Rule", menuName = "Mist Isles/Rules/Still Shore (move only when you move)")]
    public class StillShoreRule : IslandRule
    {
        [Tooltip("Creatures keep moving this long after the player stops, so the freeze reads as a reaction rather than a glitch.")]
        [SerializeField, Min(0f)] private float reactionDelay = 0.1f;

        public override bool AllowsMovement(in RuleContext ctx)
        {
            return ctx.PlayerMoving || ctx.PlayerStillTime < reactionDelay;
        }
    }
}
