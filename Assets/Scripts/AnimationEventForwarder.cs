using UnityEngine;

public class AnimationEventForwarder : MonoBehaviour
{
    public void OnFootstep(AnimationEvent animationEvent)
    {
        if (transform.parent != null)
        {
            transform.parent.SendMessage(
                "OnFootstep",
                animationEvent,
                SendMessageOptions.DontRequireReceiver
            );
        }
    }

    public void OnLand(AnimationEvent animationEvent)
    {
        if (transform.parent != null)
        {
            transform.parent.SendMessage(
                "OnLand",
                animationEvent,
                SendMessageOptions.DontRequireReceiver
            );
        }
    }
}