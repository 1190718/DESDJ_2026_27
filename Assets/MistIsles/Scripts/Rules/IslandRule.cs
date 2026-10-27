using MistIsles.Core;
using UnityEngine;

namespace MistIsles.Rules
{
    /// <summary>What a rule can look at when deciding how a creature behaves this frame.</summary>
    public readonly struct RuleContext
    {
        public readonly Vector3 PlayerPosition;
        public readonly float PlayerSpeed;
        public readonly bool PlayerMoving;
        public readonly float PlayerStillTime;
        public readonly Vector3 SelfPosition;

        public RuleContext(Vector3 playerPosition, float playerSpeed, bool playerMoving, float playerStillTime, Vector3 selfPosition)
        {
            PlayerPosition = playerPosition;
            PlayerSpeed = playerSpeed;
            PlayerMoving = playerMoving;
            PlayerStillTime = playerStillTime;
            SelfPosition = selfPosition;
        }

        public float DistanceToPlayer => Vector3.Distance(PlayerPosition, SelfPosition);

        public static RuleContext For(Transform self)
        {
            PlayerMotionTracker t = PlayerMotionTracker.Instance;
            Transform p = PlayerLocator.Player;
            return new RuleContext(
                p != null ? p.position : self.position,
                t != null ? t.Speed : 0f,
                t != null && t.IsMoving,
                t != null ? t.StillTime : 0f,
                self.position);
        }
    }

    /// <summary>
    /// An island's "invisible system". It is never shown to the player: they only see its
    /// effects on creatures and must deduce it. Each island references one rule asset, and
    /// every enemy on that island asks the rule before it moves or perceives.
    /// New rules are new subclasses; enemy code does not change.
    /// </summary>
    public abstract class IslandRule : ScriptableObject
    {
        [SerializeField] private string ruleName = "Unnamed rule";
        [Tooltip("Shown when the player buys a hint or answers the card wrongly.")]
        [SerializeField, TextArea(2, 4)] private string hint = "";
        [Tooltip("Shown once the rule is deduced on the Cartographer's Card.")]
        [SerializeField, TextArea(2, 4)] private string revealText = "";

        public string RuleName => ruleName;
        public string Hint => hint;
        public string RevealText => revealText;

        /// <summary>May the creature move (walk, turn, attack) this frame?</summary>
        public virtual bool AllowsMovement(in RuleContext ctx) => true;

        /// <summary>Multiplier applied to the creature's sight range.</summary>
        public virtual float PerceptionMultiplier(in RuleContext ctx) => 1f;

        public void Configure(string name, string hintText, string reveal)
        {
            ruleName = name;
            hint = hintText;
            revealText = reveal;
        }
    }
}
