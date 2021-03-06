﻿using System.Runtime.Serialization;
using NTM2.Controller;
using NTM2.Learning;
using NTM2.Memory.Addressing;
using NTM2.Memory.Addressing.Content;

namespace NTM2.Memory
{
    [DataContract]
    internal class NTMMemory
    {
        [DataMember]
        internal readonly Unit[][] Data;

        [DataMember]
        internal readonly HeadSetting[] HeadSettings;

        [DataMember]
        private readonly Head[] _heads;

        [DataMember]
        private readonly NTMMemory _oldMemory;
        [DataMember]
        private readonly BetaSimilarity[][] _oldSimilarities;

        [DataMember]
        private readonly double[][] _erase;
        [DataMember]
        private readonly double[][] _add;

        [DataMember]
        internal readonly int CellCountN;
        [DataMember]
        internal readonly int CellSizeM;
        [DataMember]
        internal readonly int HeadCount;

        internal NTMMemory(int cellCountN, int cellSizeM, int headCount)
        {
            CellCountN = cellCountN;
            CellSizeM = cellSizeM;
            HeadCount = headCount;
            Data = UnitFactory.GetTensor2(cellCountN, cellSizeM);
            _oldSimilarities = BetaSimilarity.GetTensor2(headCount, cellCountN);
        }

        internal NTMMemory(HeadSetting[] headSettings, Head[] heads, NTMMemory memory)
        {
            CellCountN = memory.CellCountN;
            CellSizeM = memory.CellSizeM;
            HeadCount = memory.HeadCount;
            HeadSettings = headSettings;
            _heads = heads;
            _oldMemory = memory;
            Data = UnitFactory.GetTensor2(memory.CellCountN, memory.CellSizeM);

            _erase = GetTensor2(HeadCount, memory.CellSizeM);
            _add = GetTensor2(HeadCount, memory.CellSizeM);
            var erasures = GetTensor2(memory.CellCountN, memory.CellSizeM);

            for (int i = 0; i < HeadCount; i++)
            {
                Unit[] eraseVector = _heads[i].EraseVector;
                Unit[] addVector = _heads[i].AddVector;
                double[] erases = _erase[i];
                double[] adds = _add[i];

                for (int j = 0; j < CellSizeM; j++)
                {
                    erases[j] = Sigmoid.GetValue(eraseVector[j].Value);
                    adds[j] = Sigmoid.GetValue(addVector[j].Value);
                }
            }

            for (int i = 0; i < CellCountN; i++)
            {
                Unit[] oldRow = _oldMemory.Data[i];
                double[] erasure = erasures[i];
                Unit[] row = Data[i];

                for (int j = 0; j < CellSizeM; j++)
                {
                    Unit oldCell = oldRow[j];
                    double erase = 1;
                    double add = 0;
                    for (int k = 0; k < HeadCount; k++)
                    {
                        HeadSetting headSetting = HeadSettings[k];
                        double addressingValue = headSetting.AddressingVector[i].Value;
                        erase *= (1 - (addressingValue * _erase[k][j]));
                        add += addressingValue * _add[k][j];
                    }
                    erasure[j] = erase;
                    row[j].Value += (erase * oldCell.Value) + add;
                }
            }
        }

        public void BackwardErrorPropagation()
        {
            for (int i = 0; i < HeadCount; i++)
            {
                HeadSetting headSetting = HeadSettings[i];
                double[] erase = _erase[i];
                double[] add = _add[i];
                Head head = _heads[i];

                HeadSettingGradientUpdate(i, erase, add, headSetting);
                EraseAndAddGradientUpdate(i, erase, add, headSetting, head);
            }

            MemoryGradientUpdate();
        }

        private void MemoryGradientUpdate()
        {
            for (int i = 0; i < CellCountN; i++)
            {
                Unit[] oldDataVector = _oldMemory.Data[i];
                Unit[] newDataVector = Data[i];

                for (int j = 0; j < CellSizeM; j++)
                {
                    double gradient = 1;
                    for (int q = 0; q < HeadCount; q++)
                    {
                        gradient *= 1 - (HeadSettings[q].AddressingVector[i].Value*_erase[q][j]);
                    }
                    oldDataVector[j].Gradient += gradient*newDataVector[j].Gradient;
                }
            }
        }

        private void EraseAndAddGradientUpdate(int headIndex, double[] erase, double[] add, HeadSetting headSetting, Head head)
        {
            Unit[] addVector = head.AddVector;
            for (int j = 0; j < CellSizeM; j++)
            {
                double gradientErase = 0;
                double gradientAdd = 0;
                for (int k = 0; k < CellCountN; k++)
                {
                    Unit[] row = Data[k];
                    double itemGradient = row[j].Gradient;
                    double addressingVectorItemValue = headSetting.AddressingVector[k].Value;

                    //Gradient of Erase vector
                    double gradientErase2 = _oldMemory.Data[k][j].Value;
                    for (int q = 0; q < HeadCount; q++)
                    {
                        if (q == headIndex)
                        {
                            continue;
                        }
                        gradientErase2 *= 1 - (HeadSettings[q].AddressingVector[k].Value * _erase[q][j]);
                    }
                    gradientErase += itemGradient * gradientErase2 * (-addressingVectorItemValue);
                    //Gradient of Add vector
                    gradientAdd += itemGradient * addressingVectorItemValue;
                }

                double e = erase[j];
                head.EraseVector[j].Gradient += gradientErase * e * (1 - e);

                double a = add[j];
                addVector[j].Gradient += gradientAdd * a * (1 - a);
            }
        }

        private void HeadSettingGradientUpdate(int headIndex, double[] erase, double[] add, HeadSetting headSetting)
        {
            //Gradient of head settings
            for (int j = 0; j < CellCountN; j++)
            {
                Unit[] row = Data[j];
                Unit[] oldRow = _oldMemory.Data[j];
                double gradient = 0;
                for (int k = 0; k < CellSizeM; k++)
                {
                    Unit data = row[k];
                    double oldDataValue = oldRow[k].Value;
                    for (int q = 0; q < HeadCount; q++)
                    {
                        if (q == headIndex)
                        {
                            continue;
                        }
                        HeadSetting setting = HeadSettings[q];
                        oldDataValue *= (1 - (setting.AddressingVector[j].Value * _erase[q][k]));
                    }
                    gradient += ((oldDataValue * (-erase[k])) + add[k]) * data.Gradient;
                }
                headSetting.AddressingVector[j].Gradient += gradient;
            }
        }

        public ContentAddressing[] GetContentAddressing()
        {
            return ContentAddressing.GetVector(HeadCount, i => _oldSimilarities[i]);
        }

        public void UpdateWeights(IWeightUpdater weightUpdater)
        {
            foreach (BetaSimilarity[] betaSimilarities in _oldSimilarities)
            {
                foreach (BetaSimilarity betaSimilarity in betaSimilarities)
                {
                    weightUpdater.UpdateWeight(betaSimilarity.BetaSimilarityMeasure);
                }
            }

            weightUpdater.UpdateWeight(Data);
        }

        private double[][] GetTensor2(int x, int y)
        {
            double[][] tensor = new double[x][];
            for (int i = 0; i < x; i++)
            {
                tensor[i] = new double[y];
            }
            return tensor;
        }
    }
}
