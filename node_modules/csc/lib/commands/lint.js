'use strict'

const { CLIEngine } = require('eslint')
const log = require('eslint/lib/shared/logging')
const detailedFormatter = require('eslint-detailed-reporter/lib/detailed')
const friendlyFormatter = require('eslint-friendly-formatter')
const prettyFormatter = require('eslint-formatter-pretty')

const command = 'lint [files..]'

const desc = 'Lint files'

const builder = {
  fix: {
    default: false,
    type: 'boolean',
  },
  format: {
    default: 'pretty',
    choices: [
      'friendly',
      'html-detailed',
      'pretty',
      // eslint built-in formatter
      'checkstyle',
      'codeframe',
      'compact',
      'html',
      'jslint-xml',
      'json',
      'junit',
      'stylish',
      'table',
      'tap',
      'unix',
      'visualstudio',
    ],
  },
}

function getFormatter(format, engine) {
  switch (format) {
    case 'html-detailed':
      return detailedFormatter
    case 'pretty':
      return prettyFormatter
    case 'friendly':
      return friendlyFormatter
    default:
      try {
        return engine.getFormatter(format)
      } catch {
        return friendlyFormatter
      }
  }
}

function handler(argv) {
  const files = argv.files.slice(2)
  if (files.length === 0) {
    files.push('.')
  }

  const engine = new CLIEngine({
    extensions: [
      '.erb',
      '.hbs',
      '.htm',
      '.html',
      '.js',
      '.md',
      '.mustache',
      '.nunjucks',
      '.php',
      '.vue',
    ],
    useEslintrc: true,
    fix: argv.fix,
    ignore: true,
    allowInlineConfig: true,
  })
  const report = engine.executeOnFiles(files)
  if (argv.fix) {
    CLIEngine.outputFixes(report)
  }
  const output = getFormatter(argv.format, engine)(report.results)
  if (output) {
    log.info(output)
  }
  process.exitCode = report.errorCount ? 1 : 0
}

module.exports = {
  command,
  desc,
  builder,
  handler,
}
