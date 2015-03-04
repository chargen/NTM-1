﻿using System;
using System.Diagnostics;
using System.Linq;
using NTM2;
using NTM2.Controller;
using NTM2.Learning;
using YoVisionClient;
using YoVisionCore;
using YoVisionCore.DataTypes;

namespace CopyTaskTest
{
    class Program
    {
        static void Main()
        {
            DataStream reportStream = null;
            try
            {
                YoVisionClientHelper yoVisionClientHelper = new YoVisionClientHelper();
                yoVisionClientHelper.Connect(EndpointType.NetTcp, 8081, "localhost", "YoVisionServer");
                reportStream = yoVisionClientHelper.RegisterDataStream("Copy task training",
                    new Int32DataType("Iteration"),
                    new DoubleDataType("Average data loss"),
                    new Int32DataType("Training time"),
                    new Int32DataType("Sequence length"));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            
            
            double[] errors = new double[100];
            long[] times = new long[100];
            for (int i = 0; i < 100; i++)
            {
                errors[i] = 1;
            }
            const int seed = 32702;
            Console.WriteLine(seed);
            //TODO args parsing shit
            Random rand = new Random(seed);

            const int vectorSize = 8;
            const int controllerSize = 100;
            const int headsCount = 1;
            const int memoryN = 128;
            const int memoryM = 20;

            NTMController controller = new NTMController(vectorSize + 2, vectorSize, controllerSize, headsCount,
                                                         memoryN, memoryM);
            //Randomize weights
            controller.UpdateWeights(unit => unit.Value = (rand.NextDouble() - 0.5));

            Console.WriteLine(controller.WeightsCount);

            RMSPropTeacher rmsPropTeacher = new RMSPropTeacher(controller);
            for (int i = 1; i < 10000; i++)
            {
                Tuple<double[][], double[][]> sequence = SequenceGenerator.GenerateSequence(rand.Next(20) + 1,
                                                                                            vectorSize);
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                TrainableNTM[] machines = rmsPropTeacher.Train(sequence.Item1, sequence.Item2, 0.95, 0.5, 0.001, 0.001);
                stopwatch.Stop();
                times[i%100] = stopwatch.ElapsedMilliseconds;

                double error = CalculateLogLoss(sequence.Item2, machines);
                double averageError = error / (sequence.Item2.Length * sequence.Item2[0].Length);

                errors[i % 100] = averageError;

                if (reportStream != null)
                {
                    reportStream.Set("Iteration", i);
                    reportStream.Set("Average data loss", averageError);
                    reportStream.Set("Training time", stopwatch.ElapsedMilliseconds);
                    reportStream.Set("Sequence length", (sequence.Item1.Length - 2)/2);
                    reportStream.SendData();
                }

                if (i % 100 == 0)
                {
                    Console.WriteLine("Iteration: {0}, average error: {1}, iterations per second: {2:0.0}", i, errors.Average(), 1000/times.Average());
                }
            }

        }

        private static double CalculateLogLoss(double[][] knownOutput, TrainableNTM[] trainedMachines)
        {
            double totalLoss = 0;
            int okt = knownOutput.Length - ((knownOutput.Length - 2) / 2);
            for (int t = 0; t < knownOutput.Length; t++)
            {
                for (int i = 0; i < knownOutput[t].Length; i++)
                {
                    double expected = knownOutput[t][i];
                    double real = trainedMachines[t].Controller.Output[i].Value;
                    if (t >= okt)
                    {
                        totalLoss += (expected * Math.Log(real, 2)) + ((1 - expected) * Math.Log(1 - real, 2));
                    }
                }
            }
            return -totalLoss;
        }
    }
}
