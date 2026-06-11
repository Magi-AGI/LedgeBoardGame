using MagiGameServer.Contracts.Core;

namespace MagiGameServer.Contracts.Rules
{
    /// Optional pre-Apply hook a game module can implement to reject an
    /// action envelope before it reaches the rules layer. The rules
    /// adapter only sees the payload, so cross-cutting "this seat is
    /// allowed to submit this action" checks — ownership of a player
    /// slot, permission to play on another seat's turn, etc. — can't be
    /// expressed purely in Apply. Modules that don't need pre-validation
    /// skip this interface entirely.
    ///
    /// Return null (or an empty string) to accept the submission; return
    /// a short machine-readable reason code to reject. The host treats
    /// rejection as a protocol-level error and bounces an ErrorEnvelope
    /// with that reason, without invoking Apply. Reasons are snake_case
    /// by convention so the client's dispatcher can branch on them
    /// without parsing a user-facing message.
    public interface IActionSubmissionValidator
    {
        string ValidateSubmission(SeatId submittingSeat, object action);
    }
}
