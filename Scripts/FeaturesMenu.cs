using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FeaturesMenu : MonoBehaviour
{
    public void Open(bool saveMode)
    {
        gameObject.SetActive(true);
        HexMapCamera.Locked = true;
    }

    public void Close()
    {
        gameObject.SetActive(false);
        HexMapCamera.Locked = false;
    }
}
