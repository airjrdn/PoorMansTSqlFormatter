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
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Web;
using System.Web.Services;
using System.Web.Services.Protocols;

namespace PoorMansTSqlFormatterWebDemo
{
    [WebService(Namespace = "http://www.architectshack.com/PoorMansTSqlFormatterWebDemo/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [ToolboxItem(false)]
    public class FormatterService : System.Web.Services.WebService
    {
        [WebMethod]
        public string FormatTSql(string inputString, string indentString, bool expandCommaLists, bool trailingCommas, bool expandBooleanExpressions)
        {
            //free use is all very nice, but I REALLY don't want anyone linking to this web service from some 
            // other site or app: they should just download the library and incorporate or host it directly.
            // (assuming the project is GPL-compatible)
            //
            string allowedHost = System.Configuration.ConfigurationSettings.AppSettings["ReferrerHostValidation"];
            //no error handling, just do the bare (safe) minimum.
            if (string.IsNullOrEmpty(allowedHost)
                || (Context.Request.UrlReferrer != null
                    && Context.Request.UrlReferrer.Host != null
                    && Context.Request.UrlReferrer.Host.Equals(allowedHost)
                    )
                )
            {
                PoorMansTSqlFormatterLib.SqlParseManager manager = new PoorMansTSqlFormatterLib.SqlParseManager(
                    new PoorMansTSqlFormatterLib.Tokenizers.TSqlStandardTokenizer(),
                    new PoorMansTSqlFormatterLib.Parsers.TSqlStandardParser(),
                    new PoorMansTSqlFormatterLib.Formatters.TSqlStandardFormatter(indentString, expandCommaLists, trailingCommas, expandBooleanExpressions)
                    );
                return manager.Format(inputString);
            }
            else
            {
                return string.Format("Sorry, this web service can only be called from code hosted at {0}.", allowedHost);
            }
        }
    }
}