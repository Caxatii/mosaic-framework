using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class FlowStateTests
    {
        private sealed class TestState : FlowState<TestState>
        {
            public TestState(StateMachine<TestState> machine) : base(machine) { }

            public int Entered;
            public int Exited;

            public void GoTo(TestState next) => Go(next);

            protected override void OnEnter() => Entered++;

            protected override void OnExit() => Exited++;
        }

        private StateMachine<TestState> _machine;

        [SetUp]
        public void SetUp() => _machine = new StateMachine<TestState>();

        [Test]
        public void Active_state_goes_to_next()
        {
            var first = new TestState(_machine);
            var second = new TestState(_machine);
            _machine.Change(first);

            first.GoTo(second);

            Assert.That(_machine.Current, Is.SameAs(second));
            Assert.That(first.IsActive, Is.False);
            Assert.That(second.IsActive, Is.True);
            Assert.That((first.Exited, second.Entered), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Left_state_ignores_requests()
        {
            var first = new TestState(_machine);
            var second = new TestState(_machine);
            var late = new TestState(_machine);
            _machine.Change(first);
            first.GoTo(second);

            first.GoTo(late);

            Assert.That(_machine.Current, Is.SameAs(second));
            Assert.That(late.Entered, Is.Zero);
        }
    }
}
