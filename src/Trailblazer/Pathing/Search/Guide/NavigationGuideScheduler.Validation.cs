//=======================================================================
// NavigationGuideScheduler.Validation.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Advances connector validation within the scheduled guide work budget.</content>
internal sealed partial class NavigationGuideScheduler
{
    private NavigationValidationInterest? _validationFirst;
    private NavigationValidationInterest? _validationLast;
    private long _validationCount;
    private bool _validationTurn = true;

    internal long RetainedValidationInterestCount => _validationCount;

    internal bool RegisterConnectorValidation(NavigationValidationInterest interest, NavigationGuideLease guide)
    {
        lock (_sync)
        {
            if (_closed || !guide.HasConnector || !RegisterValidation(interest))
                return false;
            // Publish the teardown alias atomically with eligibility, against Reset/Dispose.
            interest.ConnectorGuide = guide;
            return true;
        }
    }

    internal NavigationGuideLease? RegisterTopDownConnectorValidation(NavigationValidationInterest interest,
        NavigationGuideLease guide, ref NavigationGuideStatus status)
    {
        lock (_sync)
        {
            if (!RegisterValidation(interest))
            {
                guide.Dispose();
                status = NavigationGuideStatus.CapacityExceeded;
                return null;
            }
            interest.ConnectorGuide = guide;
            return guide;
        }
    }

    // Only callers owning a completed ticket or a guide needing validation may register.
    // This capacity accommodates all such owners simultaneously, including recurring connectors.
    internal bool RegisterValidation(NavigationValidationInterest interest)
    {
        lock (_sync)
        {
            if (interest.Owner == this)
                return true;
            ChronicleGuard.ThrowIfTrue(interest.Owner != null, message: "Validation interest belongs to another context.");
            long capacity = (long)_settings.MaxRetainedRequests
                + _context.Settings.QueryLimits.MaxAStarActivePayloadLeases
                + _context.Settings.QueryLimits.MaxFlowActivePayloadLeases;
            if (_closed || _validationCount >= capacity)
                return false;
            interest.Owner = this;
            AppendValidation(interest);
            _validationCount++;
            return true;
        }
    }

    internal void ReleaseValidation(NavigationValidationInterest interest)
    {
        lock (_sync)
        {
            if (interest.Owner != this)
                return;
            UnlinkValidation(interest);
            interest.Owner = null;
            interest.GrantedLifetime = null;
            interest.ConnectorGuide = default;
            _validationCount--;
        }
    }

    internal bool TryConsumeValidationTurn(NavigationValidationInterest interest)
    {
        lock (_sync)
        {
            if (interest.Owner != this || interest.GrantedFrame != _context.FrameCount
                || !ReferenceEquals(interest.GrantedLifetime, _context.ClockLifetime))
                return false;
            interest.GrantedLifetime = null;
            return true;
        }
    }

    private void GrantValidationTurns()
    {
        bool queueWork = _active != null || _scan != null || _cohortCount != 0 || _grouping;
        int allowance = queueWork ? (int)(((long)_settings.OperationStepLimit + 1) / 2)
            : _settings.OperationStepLimit;
        if (_settings.OperationStepLimit == 1 && queueWork && _validationCount != 0)
        {
            allowance = _validationTurn ? 1 : 0;
            _validationTurn = !_validationTurn;
        }
        long turns = System.Math.Min(_validationCount, allowance);
        for (long i = 0; i < turns; i++)
        {
            NavigationValidationInterest interest = _validationFirst!;
            UnlinkValidation(interest);
            AppendValidation(interest);
            // Selection and its permitted atomic validation are one charged turn.
            // An unused grant expires; it cannot be reclaimed or refilled this frame.
            interest.GrantedLifetime = _context.ClockLifetime;
            interest.GrantedFrame = _context.FrameCount;
            LastFrameOperationCount++;
        }
    }

    private void AppendValidation(NavigationValidationInterest interest)
    {
        interest.Previous = _validationLast;
        interest.Next = null;
        if (_validationLast == null)
            _validationFirst = interest;
        else
            _validationLast.Next = interest;
        _validationLast = interest;
    }

    private void UnlinkValidation(NavigationValidationInterest interest)
    {
        if (interest.Previous == null)
            _validationFirst = interest.Next;
        else
            interest.Previous.Next = interest.Next;
        if (interest.Next == null)
            _validationLast = interest.Previous;
        else
            interest.Next.Previous = interest.Previous;
        interest.Previous = interest.Next = null;
    }
}
