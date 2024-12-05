'use strict'

const chalk = require('chalk')
const updateNotifier = require('update-notifier')
const yargs = require('yargs')
const yargsParser = require('yargs-parser')

const pkg = require('../package.json')

module.exports = () => {
  updateNotifier({ pkg }).notify()

  const args = process.argv
  const parsed = yargsParser(args)
  if (parsed._.length === 2 && !parsed.help) {
    args.splice(2, 0, 'lint')
  }
  const parser = yargs
    .usage(`${chalk.bold('Usage:')} $0 <command> ${chalk.blue('[options]')}`)
    .commandDir('commands')
    .help('h')
    .version()
    .alias('h', 'help')
    .recommendCommands()
  const opts = parser.parse(args)

  if (opts._.length === 0) {
    parser.showHelp()
  }
}
