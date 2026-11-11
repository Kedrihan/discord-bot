using System.IO.Abstractions.TestingHelpers;
using DiscordBot.DTO;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

[TestFixture]
public class TU_CalendarSchedulerEngineTests
{
    private FakeTimeProvider _timeProvider = null!;
    private readonly NullLogger<CalendarSchedulerEngine> _logger = NullLogger<CalendarSchedulerEngine>.Instance;

    [SetUp]
    public void SetUp()
    {
        // 2026-11-09 is a Monday. Paris is in CET (UTC+1) in November.
        // 06:30 UTC = 07:30 Paris time.
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 6, 30, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldTriggerJob_WhenScheduleMatchesCurrentDayAndTime()
    {
        // Arrange
        var job = new TestCalendarJob(
            "TestJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(1);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldNotTrigger_WhenJobIsDisabled()
    {
        // Arrange
        var job = new TestCalendarJob(
            "DisabledJob",
            new CalendarScheduleDto(false, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(0);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldNotTrigger_WhenDayDoesNotMatch()
    {
        // Arrange (scheduled on Tuesday, current day is Monday)
        var job = new TestCalendarJob(
            "TuesdayJob",
            new CalendarScheduleDto(true, [DayOfWeek.Tuesday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(0);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldNotTrigger_WhenHourDoesNotMatch()
    {
        // Arrange (scheduled at 08:30, current time is 07:30)
        var job = new TestCalendarJob(
            "LaterHourJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(8, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(0);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldNotTrigger_WhenMinuteDoesNotMatch()
    {
        // Arrange (scheduled at 07:45, current time is 07:30)
        var job = new TestCalendarJob(
            "LaterMinuteJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 45)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(0);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldNotTrigger_WhenScheduleDaysIsEmpty()
    {
        // Arrange (empty days list)
        var job = new TestCalendarJob(
            "EmptyDaysJob",
            new CalendarScheduleDto(true, [], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(0);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldNotTrigger_WhenScheduleDaysIsNull()
    {
        // Arrange (null days list)
        var job = new TestCalendarJob(
            "NullDaysJob",
            new CalendarScheduleDto(true, null, new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job.ExecutionCount.Should().Be(0);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldDeduplicate_WhenCalledMultipleTimesInSameMinute()
    {
        // Arrange
        var job = new TestCalendarJob(
            "DeduplicatedJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act: First call at 07:30:00
        await engine.CheckAndTriggerJobsAsync();

        // Advance by 15 seconds within the same minute (07:30:15)
        _timeProvider.Advance(TimeSpan.FromSeconds(15));
        await engine.CheckAndTriggerJobsAsync();

        // Advance another 20 seconds (07:30:35)
        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        await engine.CheckAndTriggerJobsAsync();

        // Assert: Job must only have been executed once
        job.ExecutionCount.Should().Be(1);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldTriggerAgain_WhenMinuteKeyChanges()
    {
        // Arrange
        var job = new TestCalendarJob(
            "RecurringJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act: Trigger on Monday 2026-11-09 07:30
        await engine.CheckAndTriggerJobsAsync();
        job.ExecutionCount.Should().Be(1);

        // Advance 7 days to next Monday 2026-11-16 07:30
        _timeProvider.Advance(TimeSpan.FromDays(7));
        await engine.CheckAndTriggerJobsAsync();

        // Assert: Job executed a second time
        job.ExecutionCount.Should().Be(2);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldTriggerMultipleJobs_WhenBothMatchCurrentSchedule()
    {
        // Arrange
        var job1 = new TestCalendarJob(
            "Job1",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var job2 = new TestCalendarJob(
            "Job2",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job1, job2], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        job1.ExecutionCount.Should().Be(1);
        job2.ExecutionCount.Should().Be(1);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldIsolateErrors_WhenOneJobThrowsInExecuteAsync()
    {
        // Arrange
        var faultyJob = new TestCalendarJob(
            "FaultyJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)),
            () => throw new InvalidOperationException("Simulation de crash d'exécution"));

        var healthyJob = new TestCalendarJob(
            "HealthyJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));

        var engine = new CalendarSchedulerEngine([faultyJob, healthyJob], _timeProvider, _logger);

        // Act
        var act = async () => await engine.CheckAndTriggerJobsAsync();

        // Assert: The engine must not rethrow the exception, and the healthy job must run
        await act.Should().NotThrowAsync();
        faultyJob.ExecutionCount.Should().Be(1);
        healthyJob.ExecutionCount.Should().Be(1);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldIsolateErrors_WhenOneJobThrowsInGetSchedule()
    {
        // Arrange
        var faultyJob = new TestCalendarJob(
            "ScheduleCrashJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)))
        {
            ScheduleFactory = () => throw new FormatException("Erreur de format de configuration")
        };

        var healthyJob = new TestCalendarJob(
            "HealthyJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));

        var engine = new CalendarSchedulerEngine([faultyJob, healthyJob], _timeProvider, _logger);

        // Act
        var act = async () => await engine.CheckAndTriggerJobsAsync();

        // Assert: The healthy job must still run despite the broken job throwing during schedule evaluation
        await act.Should().NotThrowAsync();
        healthyJob.ExecutionCount.Should().Be(1);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldContinueNormally_WhenJobReturnsFailureResult()
    {
        // Arrange
        var failingJob = new TestCalendarJob(
            "FailingJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)),
            () => Task.FromResult(CalendarJobExecutionResultDto.Failed("Erreur réseau temporaire")));

        var healthyJob = new TestCalendarJob(
            "HealthyJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));

        var engine = new CalendarSchedulerEngine([failingJob, healthyJob], _timeProvider, _logger);

        // Act
        await engine.CheckAndTriggerJobsAsync();

        // Assert
        failingJob.ExecutionCount.Should().Be(1);
        healthyJob.ExecutionCount.Should().Be(1);
    }

    [Test]
    public void MorningBriefingJob_GetSchedule_ShouldReflectConfigServiceSettings()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var configService = new MorningBriefingConfigService(fileSystem, NullLogger<MorningBriefingConfigService>.Instance);
        var job = new MorningBriefingJob(configService, null!);

        // Act
        var schedule = job.GetSchedule();

        // Assert
        job.JobName.Should().Be("Morning Briefing Vélo");
        schedule.IsEnabled.Should().BeTrue();
        schedule.ScheduleDays.Should().Contain([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday]);
        schedule.ScheduleTime.Should().Be(new TimeOnly(7, 0));
    }

    [Test]
    public void TechWatchJob_GetSchedule_ShouldReflectConfigServiceSettings()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        var configuration = new ConfigurationBuilder().Build();
        var configService = new TechWatchConfigService(fileSystem, configuration, NullLogger<TechWatchConfigService>.Instance);
        var job = new TechWatchJob(configService, null!);

        // Act
        var schedule = job.GetSchedule();

        // Assert
        job.JobName.Should().Be("Veille Technologique");
        schedule.IsEnabled.Should().BeTrue();
        schedule.ScheduleDays.Should().Contain([DayOfWeek.Monday]);
        schedule.ScheduleTime.Should().Be(new TimeOnly(7, 30));
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldPreventConcurrentExecution_WhenSameJobIsAlreadyRunning()
    {
        // Arrange: A job that blocks until released
        var tcs = new TaskCompletionSource<CalendarJobExecutionResultDto>();
        var slowJob = new TestCalendarJob(
            "SlowJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)),
            () => tcs.Task);

        var engine = new CalendarSchedulerEngine([slowJob], _timeProvider, _logger);

        // Act 1: First trigger (job starts and is running)
        var firstExecutionTask = engine.CheckAndTriggerJobsAsync();

        // Advance by 7 days to next Monday 07:30 while job is still executing
        _timeProvider.Advance(TimeSpan.FromDays(7));
        await engine.CheckAndTriggerJobsAsync();

        // Assert: The second trigger must have been skipped because the semaphore was held
        slowJob.ExecutionCount.Should().Be(1);

        // Release the first execution
        tcs.SetResult(CalendarJobExecutionResultDto.Succeeded());
        await firstExecutionTask;
        slowJob.ExecutionCount.Should().Be(1);
    }

    [Test]
    public async Task CheckAndTriggerJobsAsync_ShouldHandleJobTimeout_WhenJobExceedsTimeoutLimit()
    {
        // Arrange: A job that awaits until canceled by token
        var timedOutJob = new TestCalendarJob(
            "TimedOutJob",
            new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)),
            null,
            async token =>
            {
                // Wait indefinitely until cancellation token fires
                var tcs = new TaskCompletionSource();
                using var reg = token.Register(() => tcs.TrySetResult());
                await tcs.Task;
                return CalendarJobExecutionResultDto.Failed("Timeout");
            });

        var engine = new CalendarSchedulerEngine([timedOutJob], _timeProvider, _logger);

        // Act: Start execution
        var runTask = engine.CheckAndTriggerJobsAsync();

        // Advance timeProvider past the 10-minute timeout
        _timeProvider.Advance(TimeSpan.FromMinutes(11));

        var act = async () => await runTask;

        // Assert: Engine must handle timeout gracefully without throwing
        await act.Should().NotThrowAsync();
        timedOutJob.ExecutionCount.Should().Be(1);
    }

    [Test]
    public void Dispose_ShouldCleanUpSemaphoresWithoutError()
    {
        // Arrange
        var job = new TestCalendarJob("DisposedJob", new CalendarScheduleDto(true, [DayOfWeek.Monday], new TimeOnly(7, 30)));
        var engine = new CalendarSchedulerEngine([job], _timeProvider, _logger);

        // Act & Assert
        var act = () => engine.Dispose();
        act.Should().NotThrow();
    }

    private class TestCalendarJob(
        string name,
        CalendarScheduleDto schedule,
        Func<Task<CalendarJobExecutionResultDto>>? executeCallback = null,
        Func<CancellationToken, Task<CalendarJobExecutionResultDto>>? executeWithTokenCallback = null) : ICalendarJob
    {
        public string JobName => name;
        public int ExecutionCount { get; private set; }
        public Func<CalendarScheduleDto>? ScheduleFactory { get; set; }

        public CalendarScheduleDto GetSchedule() => ScheduleFactory != null ? ScheduleFactory() : schedule;

        public async Task<CalendarJobExecutionResultDto> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            if (executeWithTokenCallback != null)
            {
                return await executeWithTokenCallback(cancellationToken);
            }

            if (executeCallback != null)
            {
                return await executeCallback();
            }

            return CalendarJobExecutionResultDto.Succeeded();
        }
    }
}
