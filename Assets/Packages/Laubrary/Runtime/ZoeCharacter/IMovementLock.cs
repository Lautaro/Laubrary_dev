namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// Optional capability on a character (discovered with GetComponent): "walking is not allowed right now",
    /// e.g. while a committed move plays. The motion drivers stop reading move input while it is set; they do
    /// not stop movement something else applies (a knockback, a move's own forward push).
    ///
    /// Declared here so the drivers need no knowledge of what decides it; the Zoetrope bridge supplies the
    /// implementation that answers from the character's playing reaction.
    /// </summary>
    public interface IMovementLock
    {
        bool MovementLocked { get; }
    }
}
