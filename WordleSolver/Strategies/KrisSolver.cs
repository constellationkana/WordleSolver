using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WordleSolver.Strategies
{
    /// <summary>
    /// Wordle solver that filters possible answers using feedback
    /// and chooses guesses using letter-frequency scoring.
    /// </summary>
    public sealed class KrisSolver : IWordleSolverStrategy
	{
		/// <summary>Absolute or relative path of the word-list file.</summary>
		private static readonly string WordListPath = Path.Combine("data", "wordle.txt");

		/// <summary>In-memory dictionary of valid five-letter words.</summary>
		private static readonly List<string> WordList = LoadWordList();

		/// <summary>
		/// Remaining words that can be chosen
		/// </summary>
		private List<string> _remainingWords = new();

        // TODO: ADD your own private variables that you might need
        /// <summary>
        /// A set to keep track of guessed words to avoid repeating guesses. HashSet gets answers quicker than List.Contains for large lists.
        /// This way the solver can check quickly what words have been used to save time when filtering remaining words and when choosing the next guess.
        /// </summary>
        private readonly HashSet<string> _guessedWords = new();

		/// <summary>
		/// Loads the dictionary from disk, filtering to distinct five-letter lowercase words.
		/// </summary>
		private static List<string> LoadWordList()
		{
			if (!File.Exists(WordListPath))
				throw new FileNotFoundException($"Word list not found at path: {WordListPath}");

			return File.ReadAllLines(WordListPath)
				.Select(w => w.Trim().ToLowerInvariant())
				.Where(w => w.Length == 5)
				.Distinct()
				.ToList();
		}

		/// <inheritdoc/>
		public void Reset()
		{
			// Reset guessed words and remaining words to start a new game.
			_guessedWords.Clear();
			// Make a fresh copy of the full word list for this game.
			_remainingWords = new List<string>(WordList);
		}

		/// <summary>
		/// Determines the next word to guess given feedback from the previous guess.
		/// </summary>
		/// <param name="previousResult">
		/// The <see cref="GuessResult"/> returned by the game engine for the last guess
		/// (or <see cref="GuessResult.Default"/> if this is the first turn).
		/// </param>
		/// <returns>A five-letter lowercase word.</returns>
		public string PickNextGuess(GuessResult previousResult)
		{
			if (previousResult == null)
				throw new ArgumentNullException(nameof(previousResult));

			// Analyze previousResult and remove any words from _remainingWords that aren't possible
			if (!previousResult.IsValid)
				throw new InvalidOperationException("PickNextGuess shouldn't be called if previous result isn't valid");

			// Check if first guess
			int previousGuesses = previousResult.Guesses?.Count ?? 0;
			if (previousGuesses == 0)
			{
                // Pick a reasonable starting word (must be present in WordList)
                //Pick "raise" as the first guess if it's in the word list, otherwise just pick the first word. 
                //"raise" is a popular starting word in Wordle due to its combination of common letters and vowel placement, which can provide good feedback for narrowing down possibilities.
                string firstWord = WordList.Contains("raise") ? "raise" : WordList.First();

				_guessedWords.Add(firstWord);
				_remainingWords.Remove(firstWord);

				return firstWord;
			}
			else
			{
				// Analyze the previousResult and reduce/filter _remainingWords based on the feedback
                // Main logic of solver, filters the words based on previous guess feedback
				FilterRemainingWords(previousResult);
			}

			// Utilize the remaining words to choose the next guess
			string choice = ChooseBestRemainingWord(previousResult);
			_remainingWords.Remove(choice);
			_guessedWords.Add(choice);

			return choice;

			
		}
        /// <summary>
        /// Removes words that are inconsistent with the latest feedback from the game. 
        /// "Could this word be the answer given the feedback we got for our last guess?"
        /// What makes the solver actually smart is how it narrows down the list of possible answers based on the feedback from each guess.
        /// </summary>
        /// <param name="previousResult">
        /// The latest result returned by the Wordle engine.
        /// </param> 


        private void FilterRemainingWords(GuessResult previousResult)
        {
            string lastGuess = previousResult.Word;
            LetterStatus[] actualStatuses = previousResult.LetterStatuses;

            _remainingWords = _remainingWords
                .Where(possibleAnswer => !_guessedWords.Contains(possibleAnswer))
                .Where(possibleAnswer => WouldProduceSameFeedback(lastGuess, possibleAnswer, actualStatuses))
                .ToList();
        }
        /// <summary>
        /// Checks whether a possible answer would produce the same feedback
        /// that the game gave for the previous guess.
        /// Checks whether a candidate word is still possible
        /// If fake feedback matches real, then the possible answer is consistent with the feedback and can remain in the list of candidates.
        /// </summary>
        /// <param name="guess">The word that was guessed.</param>
        /// <param name="possibleAnswer">A candidate answer being tested.</param>
        /// <param name="actualStatuses">The actual feedback from the game.</param>
        /// <returns>
        /// True if the possible answer matches the feedback; otherwise, false.
        /// </returns>
        private static bool WouldProduceSameFeedback(
            string guess,
            string possibleAnswer,
            LetterStatus[] actualStatuses)
        {
            LetterStatus[] simulatedStatuses = ScoreGuessAgainstAnswer(guess, possibleAnswer);

            for (int i = 0; i < 5; i++)
            {
                if (simulatedStatuses[i] != actualStatuses[i])
                    return false;
            }

            return true;
        }
        /// <summary>
        /// Simulates Wordle feedback for a guess against a possible answer.
        /// "If the candidate word were the real answer, what feedback would the game give for the guess?"
        /// Pretends a word is the hidden answer and scores the guess against it, returning the letter statuses that would be given by the game engine
        /// This is used to check if a possible answer is consistent with the feedback we got for our last guess. 
        /// If the simulated feedback doesn't match the actual feedback, then that possible answer can't be the hidden answer and can be removed from the remaining words list.
        /// This method has two passes:
        /// The first pass identifies letters that are correct (green) and counts the occurrences of each letter in the answer.
        /// The second pass identifies misplaced letters (yellow) and unused letters (gray) based on the counts from the first pass, ensuring that duplicate letters are handled correctly.
        /// </summary>
        /// <param name="guess">The guessed word.</param>
        /// <param name="answer">The possible hidden answer.</param>
        /// <returns>
        /// A five-item array of letter statuses.
        /// </returns>
        private static LetterStatus[] ScoreGuessAgainstAnswer(string guess, string answer)
        {
            LetterStatus[] statuses = Enumerable
                .Repeat(LetterStatus.Unused, 5)
                .ToArray();

            Dictionary<char, int> answerCharCounts = answer
                .GroupBy(c => c)
                .ToDictionary(group => group.Key, group => group.Count());

            for (int i = 0; i < 5; i++)
            {
                if (guess[i] == answer[i])
                {
                    statuses[i] = LetterStatus.Correct;
                    answerCharCounts[guess[i]]--;
                }
            }

            for (int i = 0; i < 5; i++)
            {
                if (statuses[i] == LetterStatus.Correct)
                    continue;

                if (answerCharCounts.TryGetValue(guess[i], out int count) && count > 0)
                {
                    statuses[i] = LetterStatus.Misplaced;
                    answerCharCounts[guess[i]]--;
                }
                else
                {
                    statuses[i] = LetterStatus.Unused;
                }
            }

            return statuses;
        }
        /// <summary>
        /// Pick the best of the remaining words by scoring each word using letter frequency.
        /// </summary>
        /// <param name="previousResult">
        /// The latest result returned by the game engine.
        /// </param>
        /// <returns>
        /// The highest-scoring word from the remaining possible words.
        /// </returns>
        public string ChooseBestRemainingWord(GuessResult previousResult)
        {
            if (_remainingWords.Count == 0)
                throw new InvalidOperationException("No remaining words to choose from");

            Dictionary<char, int> letterFrequency = BuildLetterFrequencyTable();

            return _remainingWords
                .Where(word => !_guessedWords.Contains(word))
                .OrderByDescending(word => ScoreWordByLetterFrequency(word, letterFrequency))
                .ThenBy(word => word)
                .First();
        }
        /// <summary>
        /// Counts how often each letter appears in the remaining possible words.
        /// </summary>
        /// <returns>
        /// A dictionary where each letter maps to how many remaining words contain that letter.
        /// </returns>
        private Dictionary<char, int> BuildLetterFrequencyTable()
        {
            Dictionary<char, int> frequency = new();

            foreach (string word in _remainingWords)
            {
                foreach (char letter in word.Distinct())
                {
                    if (!frequency.ContainsKey(letter))
                        frequency[letter] = 0;

                    frequency[letter]++;
                }
            }

            return frequency;
        }
        /// <summary>
        /// Scores a word based on how common its unique letters are among the remaining words.
        /// </summary>
        /// <param name="word">The word being scored.</param>
        /// <param name="letterFrequency">
        /// A dictionary containing letter frequencies from the remaining possible words.
        /// </param>
        /// <returns>
        /// A numeric score where higher values mean the word is likely to be more useful.
        /// </returns>
        private static int ScoreWordByLetterFrequency(string word, Dictionary<char, int> letterFrequency)
        {
            int score = 0;

            foreach (char letter in word.Distinct())
            {
                if (letterFrequency.TryGetValue(letter, out int frequency))
                    score += frequency;
            }

            int duplicatePenalty = word.Length - word.Distinct().Count();
            score -= duplicatePenalty * 10;

            return score;
        }
    }
}