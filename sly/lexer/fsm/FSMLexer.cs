﻿using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace sly.lexer.fsm
{
    public delegate void BuildExtension<IN>(IN token, LexemeAttribute lexem, GenericLexer<IN> lexer) where IN : struct;

    public class FSMMatch<N>
    {
        public char StringDelimiter = '"';

        public FSMMatch(bool success, N result = default(N), string value = null, TokenPosition position = null)
        {
            Properties = new Dictionary<string, object>();
            IsSuccess = success;
            Result = new Token<N>(result, value, position);
        }

        public Dictionary<string, object> Properties { get; set; }

        public bool IsSuccess { get; set; }

        public Token<N> Result { get; set; }
    }

    public class FSMLexer<N>
    {
        private readonly Dictionary<int, FSMNode<N>> Nodes;

        public char StringDelimiter = '"';

        private readonly Dictionary<int, List<FSMTransition>> Transitions;

        public FSMLexer()
        {
            Nodes = new Dictionary<int, FSMNode<N>>();
            Transitions = new Dictionary<int, List<FSMTransition>>();
            Callbacks = new Dictionary<int, NodeCallback<N>>();
            Actions = new Dictionary<int, NodeAction>();
            IgnoreWhiteSpace = false;
            IgnoreEOL = false;
            AggregateEOL = false;
            WhiteSpaces = new List<char>();
        }

        public bool IgnoreWhiteSpace { get; set; }

        public List<char> WhiteSpaces { get; set; }

        public bool IgnoreEOL { get; set; }

        public bool AggregateEOL { get; set; }


        private Dictionary<int, NodeCallback<N>> Callbacks { get; }

        private Dictionary<int, NodeAction> Actions { get; }


        public override string ToString()
        {
            var dump = new StringBuilder();
            foreach (var transitions in Transitions.Values)
            foreach (var transition in transitions)
                dump.AppendLine(transition.ToString());
            return dump.ToString();
        }


        #region accessors

        internal bool HasState(int state)
        {
            return Nodes.ContainsKey(state);
        }

        internal FSMNode<N> GetNode(int state)
        {
            FSMNode<N> node = null;
            Nodes.TryGetValue(state, out node);
            return node;
        }


        internal int NewNodeId => Nodes.Count;


        internal bool HasCallback(int nodeId)
        {
            return Callbacks.ContainsKey(nodeId);
        }

        internal void SetCallback(int nodeId, NodeCallback<N> callback)
        {
            Callbacks[nodeId] = callback;
        }


        internal bool HasAction(int nodeId)
        {
            return Actions.ContainsKey(nodeId);
        }

        internal void SetAction(int nodeId, NodeAction action)
        {
            Actions[nodeId] = action;
        }

        #endregion


        #region  special conf

        #endregion

        #region build

        public FSMTransition GetTransition(int nodeId, char token)
        {
            FSMTransition transition = null;
            if (HasState(nodeId))
                if (Transitions.ContainsKey(nodeId))
                {
                    var leavingTransitions = Transitions[nodeId];
                    transition = leavingTransitions.FirstOrDefault(t => t.Match(token));
                }

            return transition;
        }


        public void AddTransition(FSMTransition transition)
        {
            var transitions = new List<FSMTransition>();
            if (Transitions.ContainsKey(transition.FromNode)) transitions = Transitions[transition.FromNode];
            transitions.Add(transition);
            Transitions[transition.FromNode] = transitions;
        }


        public FSMNode<N> AddNode(N value)
        {
            var node = new FSMNode<N>(value);
            node.Id = Nodes.Count;
            Nodes[node.Id] = node;
            return node;
        }

        public FSMNode<N> AddNode()
        {
            var node = new FSMNode<N>(default(N));
            node.Id = Nodes.Count;
            Nodes[node.Id] = node;
            return node;
        }

        #endregion


        #region run

        public int CurrentPosition { get; private set; }
        public int CurrentColumn { get; private set; }
        public int CurrentLine { get; private set; }


        public void Move(int newPosition, int newLine, int newColumn)
        {
            CurrentPosition = newPosition;
            CurrentLine = newLine;
            CurrentColumn = newColumn;
        }

        public FSMMatch<N> Run(string source)
        {
            return Run(source, CurrentPosition);
        }

        public FSMMatch<N> Run(string source, int start)
        {
            var value = "";
            var result = new FSMMatch<N>(false);
            var successes = new Stack<FSMMatch<N>>();
            CurrentPosition = start;
            var currentNode = Nodes[0];
            var lastNode = 0;
            TokenPosition position = null;

            var tokenStarted = false;


            if (CurrentPosition < source.Length)
            {
                var currentToken = source[CurrentPosition];

                while (CurrentPosition < source.Length && currentNode != null)
                {
                    currentToken = source[CurrentPosition];

                    var consumeSkipped = true;

                    while (consumeSkipped && !tokenStarted && CurrentPosition < source.Length)
                    {
                        currentToken = source[CurrentPosition];
                        if (IgnoreWhiteSpace && WhiteSpaces.Contains(currentToken))
                        {
                            if (successes.Any())
                                currentNode = null;
                            else
                                currentNode = Nodes[0];
                            CurrentPosition++;
                            CurrentColumn++;
                        }
                        else
                        {
                            var eol = EOLManager.IsEndOfLine(source, CurrentPosition);

                            if (IgnoreEOL && eol != EOLType.No)
                            {
                                if (successes.Any())
                                    currentNode = null;
                                else
                                    currentNode = Nodes[0];
                                CurrentPosition += eol == EOLType.Windows ? 2 : 1;
                                CurrentColumn = 0;
                                CurrentLine++;
                            }
                            else
                            {
                                consumeSkipped = false;
                            }
                        }
                    }


                    currentNode = Move(currentNode, currentToken, value);
                    if (currentNode != null)
                    {
                        lastNode = currentNode.Id;
                        value += currentToken;

                        if (!tokenStarted)
                        {
                            tokenStarted = true;
                            position = new TokenPosition(CurrentPosition, CurrentLine, CurrentColumn);
                        }

                        if (currentNode.IsEnd)
                        {
                            var resultInter = new FSMMatch<N>(true, currentNode.Value, value, position);
                            successes.Push(resultInter);
                        }

                        if (HasAction(currentNode.Id)) value = Actions[currentNode.Id](value);
                        CurrentPosition++;
                        CurrentColumn++;
                    }
                    else
                    {
                        if (lastNode == 0 && !tokenStarted && !successes.Any() && CurrentPosition < source.Length)
                            throw new LexerException(new LexicalError(CurrentLine, CurrentColumn,
                                source[CurrentPosition]));
                        ;
                    }
                }
            }


            if (successes.Any())
            {
                result = successes.Pop();
                if (HasCallback(lastNode)) result = Callbacks[lastNode](result);
            }

            return result;
        }

        protected FSMNode<N> Move(FSMNode<N> from, char token, string value)
        {
            FSMNode<N> next = null;
            if (from != null)
                if (Transitions.ContainsKey(from.Id))
                {
                    var transitions = Transitions[from.Id];
                    if (transitions.Any())
                    {
                        var i = 0;
                        var match = false;
                        var transition = transitions[i];
                        match = transition.Match(token, value);

                        while (i < transitions.Count && !match)
                        {
                            transition = transitions[i];
                            match = transition.Match(token, value);
                            i++;
                        }

                        if (match) next = Nodes[transition.ToNode];
                    }
                }

            return next;
        }

        #endregion
    }
}