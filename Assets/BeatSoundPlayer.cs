using UnityEngine;

public class BeatSoundPlayer : MonoBehaviour
{
    [SerializeField]
    private KMAudio _audio;

    public void OnBeat(int version)
    {
        _audio.PlaySoundAtTransform(version == 1 ? "BeatHigh" : "BeatLow", transform);
    }
}
