﻿using System;
using NTM2.Memory;
using NTM2.Memory.Addressing;

namespace NTM2.Controller
{
    public class NTMController
    {
        private readonly int _memoryColumnsN;
        private readonly int _memoryRowsM;
        private readonly int _weightsCount;
        private readonly Head[] _heads;
        private readonly Unit[] _outputLayer;
        private readonly Unit[] _hiddenLayer1;
        private readonly double[] _input;
        private readonly ReadData[] _reads;
        private readonly NTMMemory _memory;

        private readonly Unit[][][] _wuh1;
        private readonly Unit[][] _wyh1;
        private readonly Unit[] _wh1b;
        private readonly Unit[][] _wh1x;
        private readonly Unit[][][] _wh1r;
        private readonly BetaSimilarity[][] _wtm1s;

        public int WeightsCount
        {
            get { return _weightsCount; }
        }

        public int HeadCount
        {
            get { return _heads.Length; }
        }

        public Head[] Heads
        {
            get { return _heads; }
        }

        public Unit[] Output
        {
            get { return _outputLayer; }
        }

        public NTMController(int inputSize, int outputSize, int controllerSize, int headCount, int memoryColumnsN, int memoryRowsM)
        {
            _memoryColumnsN = memoryColumnsN;
            _memoryRowsM = memoryRowsM;
            int headUnitSize = Head.GetUnitSize(memoryRowsM);
            _heads = new Head[headCount];
            _wtm1s = BetaSimilarity.GetTensor2(headCount, memoryColumnsN);
            _memory = new NTMMemory(memoryColumnsN, memoryRowsM);
            _wh1r = Unit.GetTensor3(controllerSize, headCount, memoryRowsM);
            _wh1x = Unit.GetTensor2(controllerSize, inputSize);
            _wh1b = Unit.GetVector(controllerSize);
            _wyh1 = Unit.GetTensor2(outputSize, controllerSize + 1);
            _wuh1 = Unit.GetTensor3(headCount, headUnitSize, controllerSize + 1);

            _weightsCount =
                (headCount * memoryColumnsN) +
                (memoryColumnsN * memoryRowsM) +
                (controllerSize * headCount * memoryRowsM) +
                (controllerSize * inputSize) +
                (controllerSize) +
                (outputSize * (controllerSize + 1)) +
                (headCount * headUnitSize * (controllerSize + 1));
        }

        private NTMController(
            int memoryColumnsN,
            int memoryRowsM,
            Unit[][][] wh1R,
            Unit[][] wh1X,
            Unit[] wh1B,
            Unit[][] wyh1,
            Unit[][][] wuh1,
            int weightsCount,
            ReadData[] readDatas,
            double[] input,
            Unit[] hiddenLayer,
            Unit[] outputLayer,
            Head[] heads)
        {
            _memoryColumnsN = memoryColumnsN;
            _memoryRowsM = memoryRowsM;
            _wh1r = wh1R;
            _wh1x = wh1X;
            _wh1b = wh1B;
            _wyh1 = wyh1;
            _wuh1 = wuh1;
            _weightsCount = weightsCount;
            _reads = readDatas;
            _input = input;
            _hiddenLayer1 = hiddenLayer;
            _outputLayer = outputLayer;
            _heads = heads;
        }

        public Ntm[] ProcessAndUpdateErrors(double[][] input, double[][] knownOutput)
        {
            //FOREACH HEAD - SET WEIGHTS TO BIAS VALUES
            ContentAddressing[] contentAddressings = ContentAddressing.GetVector(HeadCount, i => _wtm1s[i]);
            HeadSetting[] oldSettings = HeadSetting.GetVector(HeadCount, i => new Tuple<int, ContentAddressing>(_memory.MemoryColumnsN, contentAddressings[i]));
            ReadData[] readDatas = ReadData.GetVector(HeadCount, i => new Tuple<HeadSetting, NTMMemory>(oldSettings[i], _memory));

            Ntm[] machines = new Ntm[input.Length];
            Ntm empty = new Ntm(this, new MemoryState(oldSettings, readDatas, _memory));

            //BPTT
            machines[0] = new Ntm(empty, input[0]);
            for (int i = 1; i < input.Length; i++)
            {
                machines[i] = new Ntm(machines[i - 1], input[i]);
            }

            UpdateWeights(unit => unit.Gradient = 0);

            for (int i = input.Length - 1; i >= 0; i--)
            {
                Ntm machine = machines[i];
                double[] output = knownOutput[i];

                for (int j = 0; j < output.Length; j++)
                {
                    //Delta
                    machine.Controller._outputLayer[j].Gradient = machine.Controller._outputLayer[j].Value - output[j];
                }
                machine.BackwardErrorPropagation();
            }

            //Compute gradients for the bias values of internal memory and weights
            for (int i = 0; i < readDatas.Length; i++)
            {
                readDatas[i].BackwardErrorPropagation();
                for (int j = 0; j < readDatas[i].HeadSetting.Data.Length; j++)
                {
                    contentAddressings[i].Data[j].Gradient += readDatas[i].HeadSetting.Data[j].Gradient;
                }
                contentAddressings[i].BackwardErrorPropagation();
            }

            return machines;
        }

        public NTMController Process(ReadData[] readData, double[] input)
        {
            NTMController newController = new NTMController(
                _memoryColumnsN,
                _memoryRowsM,
                _wh1r,
                _wh1x,
                _wh1b,
                _wyh1,
                _wuh1,
                _weightsCount,
                readData,
                input,
                Unit.GetVector(_wh1r.Length),
                Unit.GetVector(_wyh1.Length),
                Head.GetVector(readData.Length, i => _memoryRowsM));

            newController.ForwardPropagation(readData, input);
            return newController;
        }

        //TODO readData Units are maybe not important
        private void ForwardPropagation(ReadData[] readData, double[] input)
        {
            //Foreach neuron in hidden layer
            for (int i = 0; i < _wh1r.Length; i++)
            {
                double sum = 0;

                //Foreach head
                Unit[][] headsWeights = _wh1r[i];
                for (int j = 0; j < headsWeights.Length; j++)
                {
                    //Foreach memory cell
                    Unit[] weights = headsWeights[j];
                    ReadData read = readData[j];

                    for (int k = 0; k < weights.Length; k++)
                    {
                        sum += weights[k].Value * read.Data[k].Value;
                    }
                }

                //Foreach input
                Unit[] inputWeights = _wh1x[i];
                for (int j = 0; j < inputWeights.Length; j++)
                {
                    sum += inputWeights[j].Value * input[j];
                }

                //Plus threshold
                sum += _wh1b[i].Value;

                //Set new controller unit value
                _hiddenLayer1[i].Value = Sigmoid.GetValue(sum);
            }

            //Foreach neuron in classic output layer
            for (int i = 0; i < _wyh1.Length; i++)
            {
                double sum = 0;
                Unit[] weights = _wyh1[i];

                //Foreach input from hidden layer
                for (int j = 0; j < _hiddenLayer1.Length; j++)
                {
                    sum += weights[j].Value * _hiddenLayer1[j].Value;
                }

                //Plus threshold
                sum += weights[_hiddenLayer1.Length].Value;
                _outputLayer[i].Value = Sigmoid.GetValue(sum);
            }

            //Foreach neuron in head output layer
            for (int i = 0; i < _wuh1.Length; i++)
            {
                Unit[][] headsWeights = _wuh1[i];
                Head head = _heads[i];

                for (int j = 0; j < headsWeights.Length; j++)
                {
                    double sum = 0;
                    Unit[] headWeights = headsWeights[j];
                    //Foreach input from hidden layer
                    for (int k = 0; k < _hiddenLayer1.Length; k++)
                    {
                        sum += headWeights[k].Value * _hiddenLayer1[k].Value;
                    }
                    //Plus threshold
                    sum += headWeights[_hiddenLayer1.Length].Value;
                    head[j].Value += sum;
                }
            }
        }

        public void UpdateWeights(Action<Unit> updateAction)
        {
            foreach (BetaSimilarity[] betaSimilarities in _wtm1s)
            {
                foreach (BetaSimilarity betaSimilarity in betaSimilarities)
                {
                    updateAction(betaSimilarity.Data);
                }
            }

            Action<Unit[]> vectorUpdateAction = GetVectorUpdateAction(updateAction);
            Action<Unit[][]> tensor2UpdateAction = GetTensor2UpdateAction(updateAction);
            Action<Unit[][][]> tensor3UpdateAction = GetTensor3UpdateAction(updateAction);

            tensor2UpdateAction(_memory.Data);
            tensor2UpdateAction(_wyh1);
            tensor3UpdateAction(_wuh1);
            tensor3UpdateAction(_wh1r);
            tensor2UpdateAction(_wh1x);
            vectorUpdateAction(_wh1b);
        }

        private Action<Unit[]> GetVectorUpdateAction(Action<Unit> updateAction)
        {
            return units =>
                {
                    foreach (Unit unit in units)
                    {
                        updateAction(unit);
                    }
                };
        }

        private Action<Unit[][]> GetTensor2UpdateAction(Action<Unit> updateAction)
        {
            Action<Unit[]> vectorUpdateAction = GetVectorUpdateAction(updateAction);
            return units =>
                {
                    foreach (Unit[] unit in units)
                    {
                        vectorUpdateAction(unit);
                    }
                };
        }

        private Action<Unit[][][]> GetTensor3UpdateAction(Action<Unit> updateAction)
        {
            Action<Unit[][]> tensor2UpdateAction = GetTensor2UpdateAction(updateAction);
            return units =>
                {
                    foreach (Unit[][] unit in units)
                    {
                        tensor2UpdateAction(unit);
                    }
                };
        }

        public void BackwardErrorPropagation()
        {
            //Output error backpropagation
            for (int j = 0; j < _outputLayer.Length; j++)
            {
                Unit unit = _outputLayer[j];
                Unit[] weights = _wyh1[j];
                for (int i = 0; i < _hiddenLayer1.Length; i++)
                {
                    _hiddenLayer1[i].Gradient += weights[i].Value * unit.Gradient;
                }
            }

            //Heads error backpropagation
            for (int j = 0; j < _heads.Length; j++)
            {
                Head head = _heads[j];
                Unit[][] weights = _wuh1[j];
                for (int k = 0; k < head.GetUnitSize(); k++)
                {
                    Unit unit = head[k];
                    Unit[] weightsK = weights[k];
                    for (int i = 0; i < _hiddenLayer1.Length; i++)
                    {
                        _hiddenLayer1[i].Gradient += unit.Gradient * weightsK[i].Value;
                    }
                }
            }

            //Wyh1 error backpropagation
            for (int i = 0; i < _wyh1.Length; i++)
            {
                Unit[] wyh1I = _wyh1[i];
                double yGrad = _outputLayer[i].Gradient;
                for (int j = 0; j < _hiddenLayer1.Length; j++)
                {
                    wyh1I[j].Gradient += yGrad * _hiddenLayer1[j].Value;
                }
                wyh1I[_hiddenLayer1.Length].Gradient += yGrad;
            }

            //Wuh1 error backpropagation
            for (int i = 0; i < _wuh1.Length; i++)
            {
                for (int j = 0; j < _heads[i].GetUnitSize(); j++)
                {
                    Unit headUnit = _heads[i][j];
                    Unit[] wuh1ij = _wuh1[i][j];
                    for (int k = 0; k < _hiddenLayer1.Length; k++)
                    {
                        Unit unit = _hiddenLayer1[k];
                        wuh1ij[k].Gradient += headUnit.Gradient * unit.Value;
                    }
                    wuh1ij[_hiddenLayer1.Length].Gradient += headUnit.Gradient;
                }
            }

            double[] hiddenGradients = new double[_hiddenLayer1.Length];
            for (int i = 0; i < _hiddenLayer1.Length; i++)
            {
                Unit unit = _hiddenLayer1[i];
                hiddenGradients[i] = unit.Gradient * unit.Value * (1 - unit.Value);
            }

            for (int k = 0; k < hiddenGradients.Length; k++)
            {
                Unit[][] wh1rk = _wh1r[k];
                for (int i = 0; i < _reads.Length; i++)
                {
                    ReadData readData = _reads[i];
                    Unit[] wh1rki = wh1rk[i];
                    for (int j = 0; j < wh1rki.Length; j++)
                    {
                        readData.Data[j].Gradient += hiddenGradients[k] * wh1rki[j].Value;
                    }
                }
            }

            for (int i = 0; i < _wh1r.Length; i++)
            {
                Unit[][] wh1ri = _wh1r[i];
                double hiddenGradient = hiddenGradients[i];

                for (int j = 0; j < wh1ri.Length; j++)
                {
                    Unit[] wh1rij = wh1ri[j];
                    for (int k = 0; k < _reads[j].Data.Length; k++)
                    {
                        Unit read = _reads[j].Data[k];
                        wh1rij[k].Gradient += hiddenGradient * read.Value;
                    }
                }
            }

            for (int i = 0; i < _wh1x.Length; i++)
            {
                double hiddenGradient = hiddenGradients[i];
                for (int j = 0; j < _input.Length; j++)
                {
                    double x = _input[j];
                    _wh1x[i][j].Gradient += hiddenGradient * x;
                }
            }

            for (int i = 0; i < hiddenGradients.Length; i++)
            {
                _wh1b[i].Gradient += hiddenGradients[i];
            }
        }
    }
}
