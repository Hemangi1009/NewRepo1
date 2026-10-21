using Fusion;
using UnityEngine;

public class CustomNetworkTransform : NetworkBehaviour
{
    [Networked]
    public Vector3 position { get; set; }

    [Networked]
    public Quaternion rotation { get; set; }

    public float lerpSpeed = 5;

    bool isMine => Object != null && Object == Object.HasStateAuthority;
    bool spawned;

    bool catchUp;

    public override void Spawned()
    {
        base.Spawned();

        spawned = true;

        // Initialize networked state from the current transform so non-authority
        // clients never see the default (0,0,0) and mistakenly teleport to origin.
        // Also set catchUp on non-authority to guard the first second in case
        // activation happens before the initial sync has arrived.
        if (isMine)
        {
            position = transform.position;
            rotation = transform.rotation;
        }
        else
        {
            catchUp = true;
            Invoke(nameof(DisableCatchUp), 1f);
        }
    }

    private void OnEnable()
    {
        if (Runner == null || !Runner.IsRunning || !Runner.IsConnectedToServer || !spawned)
            return;

        // Enable instant catch up
        // This is mainly due to when network transforms become enabled/disabled there can be a slight delay in the transform catching up
        // so this sets it to teleport instantly
        if (!isMine)
        {
            catchUp = true;
            Invoke(nameof(DisableCatchUp), 1f);
        }
    }

    void DisableCatchUp()
    {
        catchUp = false;
    }

    private void Update()
    {
        if (Runner == null || !Runner.IsRunning || !Runner.IsConnectedToServer)
            return;
        if (isMine)
        {
            // Only update if it has changed
            if (position != transform.position)
                position = transform.position;
            if (rotation != transform.rotation)
                rotation = transform.rotation;
        }
        else
        {
            if (catchUp) return;
            float distance = Vector3.Distance(transform.position, position);

            // If the distance is too big just teleport it there
            if (distance > 1)
            {
                transform.position = position;
                transform.rotation = rotation;
            }

            transform.position = Vector3.Lerp(transform.position, position, Time.deltaTime * lerpSpeed);
            transform.rotation = Quaternion.Lerp(transform.rotation, rotation, Time.deltaTime * lerpSpeed);
        }
    }
}