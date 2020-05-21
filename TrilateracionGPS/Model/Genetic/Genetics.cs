﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TrilateracionGPS.Model.Data;
using TrilateracionGPS.Model.Helpers;
using TrilateracionGPS.View;

namespace TrilateracionGPS.Model.Genetic
{
    class Genetics
    {
        // For random numbers
        static Random Rand;

        // Get the necessary bits for a certain variable
        public static int GetMj(double a, double b, int n)
        {
            if (b - a <= 0 || n <= 0)
                throw new ArgumentException($"El dominio de la variable es inválido: a = {a}, b = {b}, n = {n}.");

            double r = (Math.Log(b - a) + Math.Log(10) * n) / Math.Log(2);

            int integerPart = Convert.ToInt32(r);

            return r - integerPart <= 0 ? integerPart : integerPart + 1;
        }

        // Map a chromosome part that fits in 64 bits
        private static double MapValueFast(char[] chromosome, int begin, int end, double a, double b)
        {
            ulong val = 0;
            ulong vmx = 0;
            for (int i = begin; i < end; ++i)
            {
                val = (val << 1) | ((ulong)chromosome[i] - '0');
                vmx = (vmx << 1) | 1ul;
            }

            return (ulong)((b - a) * val) / (double)vmx + a;
        }

        // Map a chromosome part that doesn't fit in 64 bits
        private static double MapValueSlow(char[] chromosome, int begin, int end, double a, double b)
        {
            double val = 0;
            double vmx = 0;
            for (int i = begin; i < end; ++i)
            {
                val = val * 2 + (chromosome[i] - '0');
                vmx = vmx * 2 + 1;
            }

            return (b - a) * val / vmx + a;
        }

        // Get the mapped value of a chromosome's variable
        // The variable occupies from chromosome[begin] to chromosome[end - 1]
        public static double MapValue(char[] chromosome, int begin, int end, double a, double b)
        {
            return end - begin > 64 ? MapValueSlow(chromosome, begin, end, a, b) : MapValueFast(chromosome, begin, end, a, b);
        }

        // Generate a chromosome with n random bits
        public static char[] GenerateRandomChromosome(int n)
        {
            var r = new char[n];

            for (int i = 0; i < n; ++i)
                r[i] = Rand.Next(0, 2).ToString()[0];

            return r;
        }

        // Get the mapped values of a chromosome
        public static double[] GetMappedValues(char[] chromosome, Limit[] limits)
        {
            var mappedValues = new double[limits.Length];
            for (int i = 0, j = 0; i < mappedValues.Length; ++i)
            {
                mappedValues[i] = MapValue(chromosome, j, j + limits[i].M, limits[i].A, limits[i].B);
                j += limits[i].M;
            }

            return mappedValues;
        }

        // Check if a chromosome is valid
        public static bool CheckChromosome(char[] chromosome, Limit[] limits, Func<double, double, bool>[] restrictions)
        {
            var mappedValues = GetMappedValues(chromosome, limits);

            for (int i = 0; i < restrictions.Length; ++i)
                if (!restrictions[i](mappedValues[0], mappedValues[1]))
                    return false;

            return true;
        }

        // Generate a valid chromosome
        public static char[] GenerateChromosome(Limit[] limits, Func<double, double, bool>[] restrictions, CancellationToken timer)
        {
            char[] chromosome = null;
            bool stay = true;

            while (stay && !timer.IsCancellationRequested)
            {
                int n = 0;
                foreach (var l in limits) n += l.M;

                chromosome = GenerateRandomChromosome(n);
                stay = !CheckChromosome(chromosome, limits, restrictions);
            }

            if (timer.IsCancellationRequested)
                throw new TimeoutException();

            return chromosome;
        }

        // Generate a poblation of n valid chromosomes
        public static char[][] GeneratePoblation(Limit[] limits, Func<double, double, bool>[] restrictions, int m, CancellationToken timer)
        {
            char[][] poblation = new char[m][];

            for (int i = 0; i < m; ++i)
                poblation[i] = GenerateChromosome(limits, restrictions, timer);

            return poblation;
        }

        // Mutate a random chromosome's gen and return a new one
        public static char[] Mutate(char[] chromosome)
        {
            var r = new char[chromosome.Length];

            for (int i = 0; i < r.Length; ++i)
                r[i] = chromosome[i];

            int index = Rand.Next(0, r.Length);
            r[index] = r[index] == '1' ? '0' : '1';

            return r;
        }

        // Cross two chromosome in a random position
        public static char[] Cross(char[] parent1, char[] parent2)
        {
            int chromosomeSize = parent1.Length;
            var child = new char[chromosomeSize];
            int n = Rand.Next(0, chromosomeSize);

            for (int i = 0; i < n; ++i)
                child[i] = parent1[i];

            for (int i = n; i < chromosomeSize; ++i)
                child[i] = parent2[i];

            return child;
        }

        // Calculate the z values associated with each chromosome and return an array with them and its sum
        public static (double[], double) CalculateZValues(char[][] poblation, Limit[] limits)
        {
            double total = 0;
            var values = new double[poblation.Length];

            for (int i = 0; i < values.Length; ++i)
            {
                var mappedValues = GetMappedValues(poblation[i], limits);
                values[i] = -Restriction.Z(mappedValues[0], mappedValues[1]);
                total += values[i];
            }

            return (values, total);
        }

        // Calculate the percentages associated and the accumulates with each z value and return them
        public static (double[], double[]) CalculatePercentages(double[] values, double total)
        {
            var percentages = new double[values.Length];
            var accumulates = new double[values.Length];
            double accumulate = 0.0;

            for (int i = 0; i < values.Length; ++i)
            {
                percentages[i] = values[i] / total;
                accumulate += percentages[i];
                accumulates[i] = accumulate;
            }

            return (percentages, accumulates);
        }

        // Get the best chromosomes of a poblation
        public static char[][] GetTheBest(char[][] poblation, double[] values, double[] accumulates)
        {
            var best = new PriorityQueue();
            for (int i = 0; i < values.Length; ++i)
            {
                double r = Rand.NextDouble();
                for (int j = 0; j < values.Length; ++j)
                {
                    if (r < accumulates[j])
                    {
                        best.Push(poblation[j], values[j]);
                        break;
                    }
                }
            }

            return best.GetOnlyValues();
        }

        // Genetic round
        public static char[][] Round(char[][] poblation, Limit[] limits)
        {
            var (values, total) = CalculateZValues(poblation, limits);
            var (percentages, accumulates) = CalculatePercentages(values, total);

            return GetTheBest(poblation, percentages, accumulates);
        }

        // Regenerate the given poblation with best chromosomes, and mutation and crossover of best chromosomes
        public static void RegeneratePoblation(char[][] poblation, char[][] best, Limit[] limits, Func<double, double, bool>[] restrictions, CancellationToken timer)
        {
            int i;
            for (i = 0; i < best.Length; ++i)
                poblation[i] = best[i];

            for (int j = i; j < poblation.Length; ++j)
            {
                while (!timer.IsCancellationRequested)
                {
                    poblation[j] = Rand.Next(0, 2) == 0 ? Mutate(best[Rand.Next(0, i)]) : Cross(best[Rand.Next(0, i)], best[Rand.Next(0, i)]);

                    if (CheckChromosome(poblation[j], limits, restrictions))
                        break;
                }

                if (timer.IsCancellationRequested)
                    throw new TimeoutException();
            }

        }

        // Genetic Algorithm
        public static (int, double, double, double) Calculate(Circle[] circles, int n, int rounds, int size, double e, bool rel, Action<(int, double, double, double)> loggerTuple, Action<string> logger,CancellationToken timer)
        {
            Rand = new Random();

            var answer = (0, 0.0, 0.0, 0.0);

            Restriction.InitializeZ(circles);

            double error = Restriction.CalculateError(e, circles.Length, rel);
            logger($"Usando error: {error}");
            var restrictions = Restriction.Generate(circles, error);
            var limits = Limit.Generate(circles, n, error, logger);


            logger($"Generando población de {size} individuos...");
            var poblation = GeneratePoblation(limits, restrictions, size, timer);
            logger($"Población generada de {size} individuos...");

            for (int i = 0; i < rounds && i < 100; ++i)
            {
                var best = Round(poblation, limits);
                RegeneratePoblation(poblation, best, limits, restrictions, timer);

                var values = GetMappedValues(poblation[0], limits);

                answer = (i, values[0], values[1], Restriction.Z(values[0], values[1]));
                loggerTuple(answer);
            }

            return answer;
        }


    }
}
