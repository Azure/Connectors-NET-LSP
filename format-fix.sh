#!/bin/bash

# Auto-fix formatting issues script

echo "🔧 Fixing code formatting..."
echo ""

# Run dotnet format to fix issues
# Using --severity warn to only fix warnings and errors, not suggestions
dotnet format --severity warn --verbosity normal
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    echo ""
    echo "✅ All formatting issues have been fixed!"
    exit 0
else
    echo ""
    echo "❌ Failed to fix some formatting issues."
    echo "   Check the output above for details."
    exit $EXIT_CODE
fi
