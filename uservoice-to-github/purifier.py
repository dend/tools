"""
Module that provides a class that filters profanities
Source: https://stackoverflow.com/a/3533322

"""

__author__ = "leoluk"
__version__ = '0.0.1'

import random
import re

class ProfanitiesFilter(object):
    def __init__(self, filterlist, ignore_case=True, replacements="$@%-?!", 
                 complete=True, inside_words=False):
        """
        Inits the profanity filter.

        filterlist -- a list of regular expressions that
        matches words that are forbidden
        ignore_case -- ignore capitalization
        replacements -- string with characters to replace the forbidden word
        complete -- completely remove the word or keep the first and last char?
        inside_words -- search inside other words?

        """

        self.badwords = filterlist
        self.ignore_case = ignore_case
        self.replacements = replacements
        self.complete = complete
        self.inside_words = inside_words

    def _make_clean_word(self, length):
        """
        Generates a random replacement string of a given length
        using the chars in self.replacements.

        """
        return ''.join([random.choice(self.replacements) for i in
                  range(length)])

    def __replacer(self, match):
        value = match.group()
        if self.complete:
            return self._make_clean_word(len(value))
        else:
            return value[0]+self._make_clean_word(len(value)-2)+value[-1]

    def clean(self, text):
        """Cleans a string from profanity."""

        regexp_insidewords = {
            True: r'(%s)',
            False: r'\b(%s)\b',
            }

        regexp = (regexp_insidewords[self.inside_words] % 
                  '|'.join(self.badwords))

        r = re.compile(regexp, re.IGNORECASE if self.ignore_case else 0)

        return r.sub(self.__replacer, text)