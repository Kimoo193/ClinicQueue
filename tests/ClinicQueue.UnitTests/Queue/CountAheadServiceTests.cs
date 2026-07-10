using ClinicQueue.Application.Queue.Positioning;
using ClinicQueue.Domain.Queue;
using FluentAssertions;

namespace ClinicQueue.UnitTests.Queue;

public sealed class CountAheadServiceTests
{
    private readonly CountAheadService _sut = new(db: null!);

    [Fact]
    public void Snapshot_booked_subject_counts_confirmed_plus_lower_temps()
    {
        var sessionId = Guid.NewGuid();
        var subject = Appt(sessionId, temp: 5, status: AppointmentStatus.Booked);
        var peers = new List<Appointment>
        {
            Appt(sessionId, temp: 1, status: AppointmentStatus.Present, real: 1),
            Appt(sessionId, temp: 2, status: AppointmentStatus.Inside, real: 2),
            Appt(sessionId, temp: 3, status: AppointmentStatus.Booked),
            Appt(sessionId, temp: 4, status: AppointmentStatus.Booked),
            subject,
            Appt(sessionId, temp: 6, status: AppointmentStatus.Booked),
            Appt(sessionId, temp: 7, status: AppointmentStatus.Cancelled),
        };

        var result = _sut.ComputeFromSnapshot(subject, peers, headToClinicThreshold: 4);

        // 2 confirmed (Present/Inside) + temps 3 and 4 booked ahead = 4
        result.CountAhead.Should().Be(4);
        result.Instruction.Should().Be(QueueInstruction.HeadToClinic);
    }

    [Fact]
    public void Snapshot_confirmed_subject_counts_only_lower_real_numbers()
    {
        var sessionId = Guid.NewGuid();
        var subject = Appt(sessionId, temp: 10, status: AppointmentStatus.Present, real: 3);
        var peers = new List<Appointment>
        {
            Appt(sessionId, temp: 1, status: AppointmentStatus.Present, real: 1),
            Appt(sessionId, temp: 2, status: AppointmentStatus.Inside, real: 2),
            subject,
            Appt(sessionId, temp: 4, status: AppointmentStatus.Present, real: 4),
            Appt(sessionId, temp: 5, status: AppointmentStatus.Booked),
        };

        var result = _sut.ComputeFromSnapshot(subject, peers, headToClinicThreshold: 4);

        // Only real 1 and 2 are ahead among Present/Inside
        result.CountAhead.Should().Be(2);
        // threshold 4 → countAhead 2 is still "head to clinic"
        result.Instruction.Should().Be(QueueInstruction.HeadToClinic);
    }

    [Fact]
    public void Snapshot_mixed_statuses_ignores_terminal_rows()
    {
        var sessionId = Guid.NewGuid();
        var subject = Appt(sessionId, temp: 2, status: AppointmentStatus.Booked);
        var peers = new List<Appointment>
        {
            Appt(sessionId, temp: 1, status: AppointmentStatus.Completed, real: 1),
            Appt(sessionId, temp: 3, status: AppointmentStatus.Absent),
            Appt(sessionId, temp: 4, status: AppointmentStatus.CarriedOver),
            Appt(sessionId, temp: 5, status: AppointmentStatus.Cancelled),
            subject,
            Appt(sessionId, temp: 6, status: AppointmentStatus.Present, real: 2),
        };

        var result = _sut.ComputeFromSnapshot(subject, peers, headToClinicThreshold: 4);

        // Only the Present peer counts as confirmed ahead; no lower booked temps
        result.CountAhead.Should().Be(1);
        result.Instruction.Should().Be(QueueInstruction.HeadToClinic);
    }

    [Fact]
    public void Snapshot_booked_far_back_is_wait_home()
    {
        var sessionId = Guid.NewGuid();
        var subject = Appt(sessionId, temp: 20, status: AppointmentStatus.Booked);
        var peers = new List<Appointment>
        {
            Appt(sessionId, temp: 1, status: AppointmentStatus.Present, real: 1),
            Appt(sessionId, temp: 2, status: AppointmentStatus.Booked),
            Appt(sessionId, temp: 3, status: AppointmentStatus.Booked),
            Appt(sessionId, temp: 4, status: AppointmentStatus.Booked),
            Appt(sessionId, temp: 5, status: AppointmentStatus.Booked),
            Appt(sessionId, temp: 6, status: AppointmentStatus.Booked),
            subject,
        };

        var result = _sut.ComputeFromSnapshot(subject, peers, headToClinicThreshold: 4);

        // 1 confirmed + 5 lower booked = 6 > threshold 4
        result.CountAhead.Should().Be(6);
        result.Instruction.Should().Be(QueueInstruction.WaitHome);
    }

    private static Appointment Appt(
        Guid sessionId,
        int temp,
        AppointmentStatus status,
        int? real = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            QueueSessionId = sessionId,
            PatientMembershipId = Guid.NewGuid(),
            TemporaryNumber = temp,
            RealNumber = real,
            Status = status
        };
}
