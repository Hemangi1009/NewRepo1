//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.InputSystem;

//[System.Serializable]
//public class AnimationInputMultiplayer
//{
//    public string animationPropertyName;
//    public InputActionProperty action;
//}

//public class AnimateOnInputMultiplayer : MonoBehaviour
//{
//    public List<AnimationInputMultiplayer> animationInputs;
//    public Animator animator;

//    // Update is called once per frame
//    void Update()
//    {
//        foreach (var item in animationInputs)
//        {
//            float actionValue = item.action.action.ReadValue<float>();
//            animator.SetFloat(item.animationPropertyName, actionValue);
//        }
//    }
//}

using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class AnimationInput
{
    public string animationPropertyName;
    public InputActionProperty action;
}

public class AnimateOnInput : NetworkBehaviour
{
    public List<AnimationInput> animationInputs;
    public Animator animator;

    // Networked floats — index-matched to animationInputs
    [Networked, Capacity(8)]
    public NetworkArray<float> SyncedValues => default;

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return; // only local player reads input

        for (int i = 0; i < animationInputs.Count; i++)
        {
            SyncedValues.Set(i, animationInputs[i].action.action.ReadValue<float>());
        }
    }

    public override void Render()
    {
        // Every client (local + remote) applies synced values to the Animator each frame
        for (int i = 0; i < animationInputs.Count; i++)
        {
            animator.SetFloat(animationInputs[i].animationPropertyName, SyncedValues[i]);
        }
    }
}