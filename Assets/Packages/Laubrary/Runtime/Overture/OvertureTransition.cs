using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Laubrary.Overture
{

public enum TransitionType
{
    Enter,
    Exit,
    Both
}

public interface IOvertureTransition
{
    TransitionType transitionType { get; set; }
    bool enabled { get; }
    Task Execute(bool isEnter);
}

public abstract class OvertureTransition : MonoBehaviour, IOvertureTransition
{
    [Header("Transition Settings")]
    [SerializeField] private TransitionType _transitionType;
    
    public TransitionType transitionType
    {
        get => _transitionType;
        set => _transitionType = value;
    }

    [SerializeField] protected List<GameObject> targetObjects = new List<GameObject>();
    [SerializeField] protected float duration = 0.5f;
    [SerializeField] protected AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private TaskCompletionSource<bool> completionSource;

    public async Task Execute(bool isEnter)
    {
        completionSource = new TaskCompletionSource<bool>();
        
        if (isEnter)
        {
            EnableTransitionObjects();
        }
        
        await ExecuteTransition(isEnter);
        await completionSource.Task;
        
        if (!isEnter)
        {
            DisableTransitionObjects();
        }
    }

    protected abstract Task ExecuteTransition(bool isEnter);

    protected virtual void EnableTransitionObjects()
    {
        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                obj.SetActive(true);
            }
        }
    }
    
    protected virtual void DisableTransitionObjects()
    {
        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                obj.SetActive(false);
            }
        }
    }

    protected void Done()
    {
        completionSource?.TrySetResult(true);
    }

    protected async Task AnimateObjects(System.Func<GameObject, Task> animationFunc)
    {
        List<Task> animationTasks = new List<Task>();

        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                animationTasks.Add(animationFunc(obj));
            }
        }

        await Task.WhenAll(animationTasks);
        Done();
    }
}

} // namespace Laubrary.Overture
