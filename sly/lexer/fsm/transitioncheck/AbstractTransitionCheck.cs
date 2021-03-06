﻿namespace sly.lexer.fsm.transitioncheck
{
    public abstract class AbstractTransitionCheck
    {
        public TransitionPrecondition Precondition { get; set; }
        public abstract bool Match(char input);

        public bool Check(char input, string value)
        {
            var match = true;
            if (Precondition != null) match = Precondition(value);
            if (match) match = Match(input);
            return match;
        }
    }
}