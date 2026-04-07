#!/bin/bash

# Enhanced formatting check script with clear error reporting

echo "🔍 Checking code formatting..."
echo ""

# Run dotnet format with verbose output
# Using --severity warn to only enforce warnings and errors, not suggestions
dotnet format --verify-no-changes --severity warn --verbosity diagnostic > format-output.log 2>&1
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    echo "✅ All files are properly formatted!"
    rm -f format-output.log
    exit 0
else
    echo "❌ Formatting issues detected!"
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "📋 Files that need formatting:"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    
    # Extract and display list of files that were formatted
    grep "Formatted code file" format-output.log | sed "s/.*Formatted code file '//" | sed "s/'\.//" | while read -r file; do
        filename=$(basename "$file")
        echo "   • $filename"
    done
    
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "📝 Detailed formatting violations:"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    
    # Extract and display error lines with file paths and line numbers
    grep -E "error WHITESPACE|error IDE" format-output.log | while read -r line; do
        # Parse: /path/to/file.cs(line,col): error TYPE: message [project]
        if [[ $line =~ (.+)\(([0-9]+),([0-9]+)\):[[:space:]]error[[:space:]](.+):[[:space:]](.+)[[:space:]]\[.+\] ]]; then
            FULLPATH="${BASH_REMATCH[1]}"
            FILE=$(basename "$FULLPATH")
            LINE="${BASH_REMATCH[2]}"
            COL="${BASH_REMATCH[3]}"
            ERRORTYPE="${BASH_REMATCH[4]}"
            MESSAGE="${BASH_REMATCH[5]}"
            
            echo "📄 $FILE"
            echo "   📍 Line $LINE, Column $COL"
            echo "   ⚠️  $ERRORTYPE: $MESSAGE"
            echo ""
        fi
    done
    
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "🔧 To fix these issues, run:"
    echo "   dotnet format --severity warn"
    echo ""
    echo "📋 Full diagnostic log saved to: format-output.log"
    echo ""
    
    exit $EXIT_CODE
fi
