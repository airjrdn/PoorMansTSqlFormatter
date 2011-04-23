﻿/*
Poor Man's T-SQL Formatter - a small free Transact-SQL formatting 
library for .Net 2.0, written in C#. 
Copyright (C) 2011 Tao Klerks

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace PoorMansTSqlFormatterLib.Parsers
{
    public class TSqlStandardParser : Interfaces.ISqlTokenParser
    {
        /*
         * TODO:
         *  - support clauses in parens? (for derived tables)
         *    - UNION clauses get special formatting?
         *  
         *  - Tests
         *    - Samples illustrating all the tokens and container combinations implemented
         *    - Samples illustrating all forms of container violations
         *    - Sample requests and their XML equivalent - once the xml format is more-or-less formalized
         *    - Sample requests and their formatted versions (a few for each) - once the "standard" format is more-or-less formalized
         */

        public XmlDocument ParseSQL(XmlDocument tokenListDoc)
        {
            XmlDocument sqlTree = new XmlDocument();
            XmlElement firstStatement;
            XmlElement currentContainerNode;
            bool errorFound = false;
            bool dataShufflingForced = false;

            if (tokenListDoc.SelectSingleNode(string.Format("/{0}/@{1}[.=1]", Interfaces.Constants.ENAME_SQLTOKENS_ROOT, Interfaces.Constants.ANAME_ERRORFOUND)) != null)
                errorFound = true;

            sqlTree.AppendChild(sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_ROOT));
            firstStatement = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_STATEMENT);
            currentContainerNode = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_CLAUSE);
            firstStatement.AppendChild(currentContainerNode);
            sqlTree.DocumentElement.AppendChild(firstStatement);

            XmlNodeList tokenList = tokenListDoc.SelectNodes(string.Format("/{0}/*", Interfaces.Constants.ENAME_SQLTOKENS_ROOT));
            int tokenCount = tokenList.Count;
            int tokenID = 0;
            while (tokenID < tokenCount)
            {
                XmlElement token = (XmlElement)tokenList[tokenID];

                switch (token.Name)
                {
                    case Interfaces.Constants.ENAME_PARENS_OPEN:
                        currentContainerNode = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_PARENS, "", currentContainerNode);
                        break;

                    case Interfaces.Constants.ENAME_PARENS_CLOSE:
                        //check whether we expected to end the parens...
                        if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_PARENS))
                        {
                            currentContainerNode = (XmlElement)currentContainerNode.ParentNode;
                        }
                        else
                        {
                            BackupTokenHandlingWithError(sqlTree, Interfaces.Constants.ENAME_OTHERNODE, ")", currentContainerNode, ref errorFound);
                        }
                        break;

                    case Interfaces.Constants.ENAME_OTHERNODE:

                        //prepare multi-keyword detection by "peeking" up to 4 keywords ahead
                        List<List<XmlElement>> compoundKeywordOverflowNodes = null;
                        List<int> compoundKeywordTokenCounts = null;
                        List<string> compoundKeywordRawStrings = null;
                        string keywordMatchPhrase = GetKeywordMatchPhrase(tokenList, tokenID, ref compoundKeywordRawStrings, ref compoundKeywordTokenCounts, ref compoundKeywordOverflowNodes);
                        int keywordMatchStringsUsed = 0;

                        if (keywordMatchPhrase.StartsWith("BEGIN TRANSACTION "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            keywordMatchStringsUsed = 2;
                            ProcessCompoundKeyword(sqlTree, Interfaces.Constants.ENAME_BEGIN_TRANSACTION, ref tokenID, currentContainerNode, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);
                        }
                        else if (keywordMatchPhrase.StartsWith("BEGIN TRY "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            keywordMatchStringsUsed = 2;
                            XmlElement newTryBlock = ProcessCompoundKeyword(sqlTree, Interfaces.Constants.ENAME_TRY_BLOCK, ref tokenID, currentContainerNode, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);
                            currentContainerNode = StartNewStatement(sqlTree, newTryBlock);
                        }
                        else if (keywordMatchPhrase.StartsWith("BEGIN CATCH "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            keywordMatchStringsUsed = 2;
                            XmlElement newCatchBlock = ProcessCompoundKeyword(sqlTree, Interfaces.Constants.ENAME_CATCH_BLOCK, ref tokenID, currentContainerNode, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);
                            currentContainerNode = StartNewStatement(sqlTree, newCatchBlock);
                        }
                        else if (keywordMatchPhrase.StartsWith("BEGIN "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            XmlElement newBeginBlock = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_BEGIN_END_BLOCK, token.InnerText, currentContainerNode);
                            currentContainerNode = StartNewStatement(sqlTree, newBeginBlock);
                        }
                        else if (keywordMatchPhrase.StartsWith("CASE "))
                        {
                            currentContainerNode = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_CASE_STATEMENT, token.InnerText, currentContainerNode);
                        }
                        else if (keywordMatchPhrase.StartsWith("END TRY "))
                        {
                            EscapeAnySingleStatementContainers(ref currentContainerNode);

                            keywordMatchStringsUsed = 2;
                            string keywordString = GetCompoundKeyword(ref tokenID, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);

                            if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                                && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_TRY_BLOCK))
                            {
                                currentContainerNode.ParentNode.ParentNode.AppendChild(sqlTree.CreateTextNode(keywordString));
                                currentContainerNode = (XmlElement)currentContainerNode.ParentNode.ParentNode.ParentNode;
                            }
                            else
                            {
                                BackupTokenHandlingWithError(sqlTree, Interfaces.Constants.ENAME_OTHERNODE, keywordString, currentContainerNode, ref errorFound);
                            }
                        }
                        else if (keywordMatchPhrase.StartsWith("END CATCH "))
                        {
                            EscapeAnySingleStatementContainers(ref currentContainerNode);

                            keywordMatchStringsUsed = 2;
                            string keywordString = GetCompoundKeyword(ref tokenID, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);

                            if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                                && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_CATCH_BLOCK))
                            {
                                currentContainerNode.ParentNode.ParentNode.AppendChild(sqlTree.CreateTextNode(keywordString));
                                currentContainerNode = (XmlElement)currentContainerNode.ParentNode.ParentNode.ParentNode;
                            }
                            else
                            {
                                BackupTokenHandlingWithError(sqlTree, Interfaces.Constants.ENAME_OTHERNODE, keywordString, currentContainerNode, ref errorFound);
                            }
                        }
                        else if (keywordMatchPhrase.StartsWith("END "))
                        {
                            if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_CASE_STATEMENT))
                            {
                                currentContainerNode.AppendChild(sqlTree.CreateTextNode(token.InnerText));
                                currentContainerNode = (XmlElement)currentContainerNode.ParentNode;
                            }
                            else
                            {
                                //Begin/End block handling
                                EscapeAnySingleStatementContainers(ref currentContainerNode);

                                if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                                    && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                    && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_BEGIN_END_BLOCK))
                                {
                                    currentContainerNode.ParentNode.ParentNode.AppendChild(sqlTree.CreateTextNode(token.InnerText));
                                    currentContainerNode = (XmlElement)currentContainerNode.ParentNode.ParentNode.ParentNode;
                                }
                                else
                                {
                                    BackupTokenHandlingWithError(sqlTree, Interfaces.Constants.ENAME_OTHERNODE, token.InnerText, currentContainerNode, ref errorFound);
                                }
                            }
                        }
                        else if (keywordMatchPhrase.StartsWith("GO "))
                        {
                            EscapeAnySingleStatementContainers(ref currentContainerNode);

                            //this looks a little simplistic... might need to review.
                            if ((token.PreviousSibling == null || IsLineBreakingWhiteSpace((XmlElement)token.PreviousSibling))
                                && (token.NextSibling == null || IsLineBreakingWhiteSpace((XmlElement)token.NextSibling))
                                )
                            {
                                // we found a batch separator - were we supposed to?
                                if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                                    && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                    && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_ROOT)
                                    )
                                {
                                    XmlElement sqlRoot = (XmlElement)currentContainerNode.ParentNode.ParentNode;
                                    StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_BATCH_SEPARATOR, token.InnerText, sqlRoot);
                                    currentContainerNode = StartNewStatement(sqlTree, sqlRoot);
                                }
                                else
                                {
                                    BackupTokenHandlingWithError(sqlTree, token.Name, token.InnerText, currentContainerNode, ref errorFound);
                                }
                            }
                            else
                            {
                                StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                            }
                        }
                        else if (keywordMatchPhrase.StartsWith("JOIN "))
                        {
                            ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                        }
                        else if (keywordMatchPhrase.StartsWith("LEFT JOIN ")
                            || keywordMatchPhrase.StartsWith("RIGHT JOIN ")
                            || keywordMatchPhrase.StartsWith("INNER JOIN ")
                            || keywordMatchPhrase.StartsWith("CROSS JOIN ")
                            )
                        {
                            ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            keywordMatchStringsUsed = 2;
                            string keywordString = GetCompoundKeyword(ref tokenID, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);
                            StandardTokenHandling(sqlTree, token.Name, keywordString, currentContainerNode);
                        }
                        else if (keywordMatchPhrase.StartsWith("FULL OUTER JOIN ")
                            || keywordMatchPhrase.StartsWith("LEFT OUTER JOIN ")
                            || keywordMatchPhrase.StartsWith("RIGHT OUTER JOIN ")
                            )
                        {
                            ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            keywordMatchStringsUsed = 3;
                            string keywordString = GetCompoundKeyword(ref tokenID, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);
                            StandardTokenHandling(sqlTree, token.Name, keywordString, currentContainerNode);
                        }
                        else if (keywordMatchPhrase.StartsWith("WHILE "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            XmlElement newWhileLoop = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_WHILE_LOOP, token.InnerText, currentContainerNode);
                            currentContainerNode = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_BOOLEAN_EXPRESSION, "", newWhileLoop);
                        }
                        else if (keywordMatchPhrase.StartsWith("IF "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            XmlElement newIfStatement = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_IF_STATEMENT, token.InnerText, currentContainerNode);
                            currentContainerNode = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_BOOLEAN_EXPRESSION, "", newIfStatement);
                        }
                        else if (keywordMatchPhrase.StartsWith("ELSE "))
                        {
                            if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_CASE_STATEMENT))
                            {
                                //we don't really do anything with case statement structure yet
                                StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                            }
                            else if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                                    && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                    && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT))
                            {
                                //topmost if - just pop back one.
                                XmlElement containerIf = (XmlElement)currentContainerNode.ParentNode.ParentNode;
                                XmlElement newElseClause = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_ELSE_CLAUSE, token.InnerText, containerIf);
                                currentContainerNode = StartNewStatement(sqlTree, newElseClause);
                            }
                            else if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                                    && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                    && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_ELSE_CLAUSE)
                                    && currentContainerNode.ParentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT)
                                )
                            {
                                //not topmost if; we need to pop up the single-statement containers stack to the next "if" that doesn't have an "else".
                                XmlElement currentNode = (XmlElement)currentContainerNode.ParentNode.ParentNode.ParentNode;
                                while (currentNode != null
                                    && (currentNode.Name.Equals(Interfaces.Constants.ENAME_WHILE_LOOP)
                                        || currentNode.SelectSingleNode(Interfaces.Constants.ENAME_ELSE_CLAUSE) != null
                                        )
                                    )
                                {
                                    if (currentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                        && currentNode.ParentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT)
                                        )
                                        currentNode = (XmlElement)currentNode.ParentNode.ParentNode.ParentNode;
                                    else if (currentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                        && currentNode.ParentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_WHILE_LOOP)
                                        )
                                        currentNode = (XmlElement)currentNode.ParentNode.ParentNode.ParentNode;
                                    else if (currentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                                        && currentNode.ParentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_ELSE_CLAUSE)
                                        && currentNode.ParentNode.ParentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT)
                                        )
                                        currentNode = (XmlElement)currentNode.ParentNode.ParentNode.ParentNode.ParentNode;
                                    else
                                        currentNode = null;
                                }

                                if (currentNode != null)
                                {
                                    XmlElement newElseClause2 = StandardTokenHandling(sqlTree, Interfaces.Constants.ENAME_ELSE_CLAUSE, token.InnerText, currentNode);
                                    currentContainerNode = StartNewStatement(sqlTree, newElseClause2);
                                }
                                else
                                {
                                    BackupTokenHandlingWithError(sqlTree, token.Name, token.InnerText, currentContainerNode, ref errorFound);
                                }
                            }
                            else
                            {
                                BackupTokenHandlingWithError(sqlTree, token.Name, token.InnerText, currentContainerNode, ref errorFound);
                            }
                        }
                        else if (keywordMatchPhrase.StartsWith("INSERT INTO "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            keywordMatchStringsUsed = 2;
                            string keywordString = GetCompoundKeyword(ref tokenID, keywordMatchStringsUsed, compoundKeywordTokenCounts, compoundKeywordRawStrings);
                            StandardTokenHandling(sqlTree, token.Name, keywordString, currentContainerNode);
                        }
                        else if (keywordMatchPhrase.StartsWith("INSERT "))
                        {
                            ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                        }
                        else if (keywordMatchPhrase.StartsWith("SELECT "))
                        {
                            if (!(
                                    currentContainerNode.FirstChild.Name.Equals(Interfaces.Constants.ENAME_OTHERNODE)
                                    && currentContainerNode.FirstChild.InnerText.ToUpper().StartsWith("INSERT")
                                    )
                                )
                                ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);

                            ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                        }
                        else
                        {
                            //miscellaneous single-word tokens, which may or may not be statement starters and/or clause starters

                            //check for statements starting...
                            if (IsStatementStarter(token))
                            {
                                ConsiderStartingNewStatement(sqlTree, ref currentContainerNode);
                            }

                            //check for statements starting...
                            if (IsClauseStarter(token))
                            {
                                ConsiderStartingNewClause(sqlTree, ref currentContainerNode);
                            }

                            StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                        }

                        //handle any Overflow Nodes
                        if (keywordMatchStringsUsed > 1)
                        {
                            for (int i = 0; i < keywordMatchStringsUsed - 1; i++)
                            {
                                foreach (XmlElement overflowEntry in compoundKeywordOverflowNodes[i])
                                {
                                    currentContainerNode.AppendChild(overflowEntry);
                                    dataShufflingForced = true;
                                }
                            }
                        }

                        break;

                    case Interfaces.Constants.ENAME_ASTERISK:
                    case Interfaces.Constants.ENAME_COMMA:
                    case Interfaces.Constants.ENAME_COMMENT_MULTILINE:
                    case Interfaces.Constants.ENAME_COMMENT_SINGLELINE:
                    case Interfaces.Constants.ENAME_NSTRING:
                    case Interfaces.Constants.ENAME_OTHEROPERATOR:
                    case Interfaces.Constants.ENAME_QUOTED_IDENTIFIER:
                    case Interfaces.Constants.ENAME_STRING:
                    case Interfaces.Constants.ENAME_WHITESPACE:
                        StandardTokenHandling(sqlTree, token.Name, token.InnerText, currentContainerNode);
                        break;
                    default:
                        throw new Exception("Unrecognized element encountered!");
                }

                tokenID++;
            }

            EscapeAnySingleStatementContainers(ref currentContainerNode);
            if (!currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                || !currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                || !currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_ROOT)
                )
                errorFound = true;

            if (errorFound)
            {
                sqlTree.DocumentElement.SetAttribute(Interfaces.Constants.ANAME_ERRORFOUND, "1");
            }

            if (dataShufflingForced)
            {
                sqlTree.DocumentElement.SetAttribute(Interfaces.Constants.ANAME_DATALOSS, "1");
            }

            return sqlTree;
        }

        private string GetKeywordMatchPhrase(XmlNodeList tokenList, int tokenID, ref List<string> rawKeywordParts, ref List<int> tokenCounts, ref List<List<XmlElement>> overflowNodes)
        {
            string phrase = "";
            int phraseComponentsFound = 0;
            rawKeywordParts = new List<string>();
            overflowNodes = new List<List<XmlElement>>();
            tokenCounts = new List<int>();
            string precedingWhitespace = "";
            int originalTokenID = tokenID;

            while (tokenID < tokenList.Count && phraseComponentsFound < 3)
            {
                if (tokenList[tokenID].Name.Equals(Interfaces.Constants.ENAME_OTHERNODE))
                {
                    phrase += tokenList[tokenID].InnerText.ToUpper() + " ";
                    phraseComponentsFound++;
                    rawKeywordParts.Add(precedingWhitespace + tokenList[tokenID].InnerText);

                    tokenID++;
                    tokenCounts.Add(tokenID - originalTokenID);

                    //found a possible phrase component - skip past any upcoming whitespace or comments, keeping track.
                    overflowNodes.Add(new List<XmlElement>());
                    precedingWhitespace = "";
                    while (tokenID < tokenList.Count
                        && (tokenList[tokenID].Name.Equals(Interfaces.Constants.ENAME_WHITESPACE)
                            || tokenList[tokenID].Name.Equals(Interfaces.Constants.ENAME_COMMENT_SINGLELINE)
                            || tokenList[tokenID].Name.Equals(Interfaces.Constants.ENAME_COMMENT_MULTILINE)
                            )
                        )
                    {
                        if (tokenList[tokenID].Name.Equals(Interfaces.Constants.ENAME_WHITESPACE))
                        {
                            precedingWhitespace += tokenList[tokenID].InnerText;
                        }
                        else
                        {
                            overflowNodes[phraseComponentsFound-1].Add((XmlElement)tokenList[tokenID]);
                        }
                        tokenID++;
                    }
                }
                else
                    //we're not interested in any other node types
                    break;
            }

            return phrase;
        }

        private XmlElement StandardTokenHandling(XmlDocument sqlTree, string newElementName, string newElementValue, XmlElement currentContainerNode)
        {
            XmlElement newElement = sqlTree.CreateElement(newElementName);
            newElement.InnerText = newElementValue;
            currentContainerNode.AppendChild(newElement);
            return newElement;
        }

        private void BackupTokenHandlingWithError(XmlDocument sqlTree, string newElementName, string newElementValue, XmlElement currentContainerNode, ref bool errorFound)
        {
            XmlElement newElement = sqlTree.CreateElement(newElementName);
            newElement.InnerText = newElementValue;
            currentContainerNode.AppendChild(newElement);
#if DEBUG
            System.Diagnostics.Debugger.Break();
#endif
            errorFound = true;
        }

        private XmlElement ProcessCompoundKeyword(XmlDocument sqlTree, string newElementName, ref int tokenID, XmlElement currentContainerNode, int compoundKeywordCount, List<int> compoundKeywordTokenCounts, List<string> compoundKeywordRawStrings)
        {
            tokenID += compoundKeywordTokenCounts[compoundKeywordCount-1] - 1;
            XmlElement newElement = sqlTree.CreateElement(newElementName);
            currentContainerNode.AppendChild(newElement);
            return newElement;
        }

        private string GetCompoundKeyword(ref int tokenID, int compoundKeywordCount, List<int> compoundKeywordTokenCounts, List<string> compoundKeywordRawStrings)
        {
            tokenID += compoundKeywordTokenCounts[compoundKeywordCount - 1] - 1;
            string outString = "";
            for (int i = 0; i < compoundKeywordCount; i++)
                outString += compoundKeywordRawStrings[i];
            return outString;
        }

        private static void ConsiderStartingNewStatement(XmlDocument sqlTree, ref XmlElement currentContainerNode)
        {
            XmlElement newElement = null;
            XmlElement newElement2 = null;
            XmlElement previousContainerElement = currentContainerNode;

            if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_BOOLEAN_EXPRESSION)
                && (currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT)
                    || currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_WHILE_LOOP)
                    )
                )
            {
                //we just ended the boolean clause of an if or while, and need to pop to the first (and only) statement.
                newElement = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_STATEMENT);
                currentContainerNode.ParentNode.AppendChild(newElement);
                newElement2 = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_CLAUSE);
                newElement.AppendChild(newElement2);
                currentContainerNode = newElement2;
                MigrateApplicableComments(previousContainerElement, currentContainerNode);
            }
            else if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                && HasNonWhiteSpaceNonSingleCommentContent(currentContainerNode)
                )
            {
                EscapeAnySingleStatementContainers(ref currentContainerNode);
                XmlElement inBetweenContainerElement = currentContainerNode;
                newElement = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_STATEMENT);
                currentContainerNode.ParentNode.ParentNode.AppendChild(newElement);

                newElement2 = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_CLAUSE);
                newElement.AppendChild(newElement2);
                currentContainerNode = newElement2;

                if (!inBetweenContainerElement.Equals(previousContainerElement))
                    MigrateApplicableComments(inBetweenContainerElement, currentContainerNode);
                MigrateApplicableComments(previousContainerElement, currentContainerNode);
            }
        }

        private XmlElement StartNewStatement(XmlDocument sqlTree, XmlElement containerElement)
        {
            XmlElement newStatement = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_STATEMENT);
            containerElement.AppendChild(newStatement);
            XmlElement newClause = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_CLAUSE);
            newStatement.AppendChild(newClause);
            return newClause;
        }

        private static void ConsiderStartingNewClause(XmlDocument sqlTree, ref XmlElement currentContainerNode)
        {
            if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                && HasNonWhiteSpaceNonSingleCommentContent(currentContainerNode)
                )
            {
                XmlElement previousContainerElement = currentContainerNode;
                XmlElement newElement = sqlTree.CreateElement(Interfaces.Constants.ENAME_SQL_CLAUSE);
                currentContainerNode.ParentNode.AppendChild(newElement);
                currentContainerNode = newElement;
                MigrateApplicableComments(previousContainerElement, currentContainerNode);
            }
        }

        private static void MigrateApplicableComments(XmlElement previousContainerElement, XmlElement currentContainerNode)
        {
            XmlNode migrationCandidate = previousContainerElement.LastChild;

            while (migrationCandidate != null)
            {
                if (migrationCandidate.NodeType == XmlNodeType.Whitespace
                    || migrationCandidate.Name.Equals(Interfaces.Constants.ENAME_WHITESPACE))
                {
                    migrationCandidate = migrationCandidate.PreviousSibling;
                    continue;
                }
                else if (migrationCandidate.PreviousSibling != null
                    && (migrationCandidate.Name.Equals(Interfaces.Constants.ENAME_COMMENT_SINGLELINE)
                        || (migrationCandidate.Name.Equals(Interfaces.Constants.ENAME_COMMENT_MULTILINE)
                            && !Regex.IsMatch(migrationCandidate.InnerText, @"(\r|\n)+")
                            )
                        )
                    && (migrationCandidate.PreviousSibling.NodeType == XmlNodeType.Whitespace
                        || migrationCandidate.PreviousSibling.Name.Equals(Interfaces.Constants.ENAME_WHITESPACE)
                        || migrationCandidate.PreviousSibling.Name.Equals(Interfaces.Constants.ENAME_COMMENT_SINGLELINE)
                        || migrationCandidate.PreviousSibling.Name.Equals(Interfaces.Constants.ENAME_COMMENT_MULTILINE)
                        )
                    )
                {
                    if ((migrationCandidate.PreviousSibling.NodeType == XmlNodeType.Whitespace
                            || migrationCandidate.PreviousSibling.Name.Equals(Interfaces.Constants.ENAME_WHITESPACE)
                            )
                        && Regex.IsMatch(migrationCandidate.PreviousSibling.InnerText, @"(\r|\n)+")
                        )
                    {
                        while (!previousContainerElement.LastChild.Equals(migrationCandidate))
                        {
                            currentContainerNode.PrependChild(previousContainerElement.LastChild);
                        }
                        currentContainerNode.PrependChild(migrationCandidate);
                        migrationCandidate = previousContainerElement.LastChild;
                    }
                    else
                    {
                        migrationCandidate = migrationCandidate.PreviousSibling;
                    }
                }
                else
                {
                    migrationCandidate = null;
                }
            }
        }

        private static void EscapeAnySingleStatementContainers(ref XmlElement currentContainerNode)
        {
            while (true)
            {
                if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                    && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                    && (currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT)
                        || currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_WHILE_LOOP)
                        )
                    )
                {

                    //we just ended the one statement of an if or while, and need to pop out to a new statement at the same level as the IF or WHILE
                    currentContainerNode = (XmlElement)currentContainerNode.ParentNode.ParentNode.ParentNode;
                }
                else if (currentContainerNode.Name.Equals(Interfaces.Constants.ENAME_SQL_CLAUSE)
                    && currentContainerNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_SQL_STATEMENT)
                    && currentContainerNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_ELSE_CLAUSE)
                    && currentContainerNode.ParentNode.ParentNode.ParentNode.Name.Equals(Interfaces.Constants.ENAME_IF_STATEMENT)
                    )
                {
                    //we just ended the one and only statement in an else clause, and need to pop out to a new statement at the same level as the IF
                    currentContainerNode = (XmlElement)currentContainerNode.ParentNode.ParentNode.ParentNode.ParentNode;
                }
                else
                {
                    break;
                }
            }
        }

        private static bool IsStatementStarter(XmlElement token)
        {
            string uppercaseValue = token.InnerText.ToUpper();
            return (token.Name.Equals(Interfaces.Constants.ENAME_OTHERNODE)
                && (uppercaseValue.Equals("SELECT")
                    || uppercaseValue.Equals("DELETE")
                    || uppercaseValue.Equals("INSERT")
                    || uppercaseValue.Equals("UPDATE")
                    || uppercaseValue.Equals("IF")
                    || uppercaseValue.Equals("SET")
                    || uppercaseValue.Equals("CREATE")
                    || uppercaseValue.Equals("DROP")
                    || uppercaseValue.Equals("ALTER")
                    || uppercaseValue.Equals("TRUNCATE")
                    || uppercaseValue.Equals("DECLARE")
                    || uppercaseValue.Equals("EXEC")
                    || uppercaseValue.Equals("EXECUTE")
                    || uppercaseValue.Equals("WHILE")
                    || uppercaseValue.Equals("BREAK")
                    || uppercaseValue.Equals("CONTINUE")
                    || uppercaseValue.Equals("PRINT")
                    || uppercaseValue.Equals("USE")
                    || uppercaseValue.Equals("RETURN")
                    || uppercaseValue.Equals("WAITFOR")
                    || uppercaseValue.Equals("RAISERROR")
                    || uppercaseValue.Equals("COMMIT")
                    || uppercaseValue.Equals("OPEN")
                    || uppercaseValue.Equals("FETCH")
                    || uppercaseValue.Equals("CLOSE")
                    || uppercaseValue.Equals("DEALLOCATE")
                    )
                );
        }

        private static bool IsClauseStarter(XmlElement token)
        {
            string uppercaseValue = token.InnerText.ToUpper();
            return (token.Name.Equals(Interfaces.Constants.ENAME_OTHERNODE)
                && (uppercaseValue.Equals("INNER")
                    || uppercaseValue.Equals("LEFT")
                    || uppercaseValue.Equals("JOIN")
                    || uppercaseValue.Equals("WHERE")
                    || uppercaseValue.Equals("FROM")
                    || uppercaseValue.Equals("ORDER")
                    || uppercaseValue.Equals("GROUP")
                    || uppercaseValue.Equals("INTO")
                    || uppercaseValue.Equals("SELECT")
                    || uppercaseValue.Equals("UNION")
                    || uppercaseValue.Equals("VALUES")
                    )
                );
        }

        private static bool IsLineBreakingWhiteSpace(XmlElement token)
        {
            return (token.Name.Equals(Interfaces.Constants.ENAME_WHITESPACE) 
                && Regex.IsMatch(token.InnerText, @"(\r|\n)+"));
        }

        private static bool HasNonWhiteSpaceNonSingleCommentContent(XmlElement containerNode)
        {
            foreach (XmlElement testElement in containerNode.SelectNodes("*"))
                if (!testElement.Name.Equals(Interfaces.Constants.ENAME_WHITESPACE)
                    && !testElement.Name.Equals(Interfaces.Constants.ENAME_COMMENT_SINGLELINE)
                    && (!testElement.Name.Equals(Interfaces.Constants.ENAME_COMMENT_MULTILINE)
                        || Regex.IsMatch(testElement.InnerText, @"(\r|\n)+")
                        )
                    )
                    return true;

            return false;
        }
    }
}