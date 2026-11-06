using System.Collections.Generic;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class StateMachineTests
    {
        private sealed class TestState : IState
        {
            private readonly string _name;
            private readonly List<string> _log;

            public TestState(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public System.Action OnEnter;

            public void Enter()
            {
                _log.Add("enter " + _name);
                OnEnter?.Invoke();
            }

            public void Exit() => _log.Add("exit " + _name);

            public void Tick(float deltaTime) => _log.Add("tick " + _name);
        }

        private readonly List<string> _log = new List<string>();
        private StateMachine<TestState> _machine;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _machine = new StateMachine<TestState>();
        }

        [Test]
        public void Change_exits_old_state_then_enters_new_and_ticks_current()
        {
            var changed = new List<TestState>();
            _machine.Changed += changed.Add;
            var a = new TestState("a", _log);
            var b = new TestState("b", _log);

            _machine.Change(a);
            _machine.Change(b);
            _machine.Tick(1);

            Assert.That(_log, Is.EqualTo(new[] { "enter a", "exit a", "enter b", "tick b" }));
            Assert.That(changed, Is.EqualTo(new[] { a, b }));
            Assert.That(_machine.Current, Is.SameAs(b));
        }

        [Test]
        public void Change_requested_during_enter_runs_after_it()
        {
            var changed = new List<TestState>();
            _machine.Changed += changed.Add;
            var b = new TestState("b", _log);
            var a = new TestState("a", _log) { OnEnter = () => _machine.Change(b) };

            _machine.Change(a);

            Assert.That(_log, Is.EqualTo(new[] { "enter a", "exit a", "enter b" }));
            Assert.That(changed, Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void Stop_exits_current_state()
        {
            _machine.Change(new TestState("a", _log));

            _machine.Stop();
            _machine.Tick(1);

            Assert.That(_log, Is.EqualTo(new[] { "enter a", "exit a" }));
            Assert.That(_machine.Current, Is.Null);
        }
    }
}
