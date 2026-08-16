using UnityEngine;

namespace Laubrary.ZoeCharacter
{
    /// <summary>
    /// The platform's canonical player input facade. Every player-facing module (locomotion
    /// states, aim styles, fire styles) reads from this interface rather than from
    /// UnityEngine.Input or UnityEngine.InputSystem directly — that way a game's input layer
    /// can be swapped (network-replay, AI driver, replay scrubber, second player on the same
    /// Keyboard) by providing a different IZoeInputSource implementation, without any of the
    /// player module code changing.
    ///
    /// Implementations MUST be cheap to call every frame (called from FixedUpdate/Update).
    /// Do not allocate per call.
    /// </summary>
    public interface IZoeInputSource
    {
        /// <summary>2D move vector (already normalised on keyboard, analog on gamepad).
        /// Convention: +X = right, +Y = up. Magnitude should reflect intent (0 = not moving).</summary>
        Vector2 ReadMove();

        /// <summary>True on the frame the fire button was first pressed (edge-triggered).
        /// Use for "fire on press" semantics like a charged-shot release.</summary>
        bool FirePressed();

        /// <summary>True while the fire button is held (level-triggered).
        /// Use for "auto-fire while held" semantics like a machine gun.</summary>
        bool FireHeld();

        /// <summary>True on the frame the fire button was released.
        /// Use for "fire on release" semantics like a bow.</summary>
        bool FireReleased();

        /// <summary>Optional: the world-space cursor position (mouse, gamepad cursor).
        /// Return false if this source has no cursor concept (e.g. twin-stick aim).</summary>
        bool TryReadCursorWorld(out Vector2 worldPosition);

        /// <summary>Optional: a 2D aim direction in world space (e.g. twin-stick aim,
        /// or a cached cursor→self direction). Returns false if this source has no aim
        /// direction distinct from cursor (callers should then derive from cursor + self).</summary>
        bool TryReadAimDirection(Vector2 selfPosition, out Vector2 aimDirection);
    }
}
