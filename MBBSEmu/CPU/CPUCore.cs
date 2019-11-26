﻿using System;
using Iced.Intel;
using MBBSEmu.Logging;
using NLog;
using System.Linq;

namespace MBBSEmu.CPU
{
    public class CpuCore
    {
        protected static readonly Logger _logger = LogManager.GetCurrentClassLogger(typeof(CustomLogger));
        public delegate void InvokeExternalFunctionDelegate(int importedNameTableOrdinal, int functionOrdinal);

        private readonly InvokeExternalFunctionDelegate _invokeExternalFunctionDelegate;

        public readonly CpuRegisters Registers;
        public readonly CpuMemory Memory;

        private Instruction _currentInstruction;


        public CpuCore(InvokeExternalFunctionDelegate invokeExternalFunctionDelegate)
        {
            Registers = new CpuRegisters();
            Memory = new CpuMemory();
            _invokeExternalFunctionDelegate = invokeExternalFunctionDelegate;
        }

        public void Tick()
        {
            _currentInstruction = Memory.GetInstruction(Registers.CS, Registers.IP);

#if DEBUG
    _logger.Debug($"{_currentInstruction.ToString()}");
#endif

            switch (_currentInstruction.Mnemonic)
            {
                case Mnemonic.Add:
                    Op_Add();
                    break;
                case Mnemonic.Push:
                    Op_Push();
                    break;
                case Mnemonic.Mov:
                    Op_Mov();
                    break;
                case Mnemonic.Call:
                    Op_Call();
                    break;
            }

            Registers.IP += _currentInstruction.ByteLength;
        }

        private void Op_Add()
        {
            switch (_currentInstruction.Op0Kind)
            {
                case OpKind.Register when _currentInstruction.Op1Kind == OpKind.Register:
                    Registers.SetValue(_currentInstruction.Op0Register,
                        Registers.GetValue(_currentInstruction.Op1Register));
                    break;
                
                case OpKind.Register when _currentInstruction.Op1Kind == OpKind.Immediate8:
                case OpKind.Register when _currentInstruction.Op1Kind == OpKind.Immediate8to16:
                case OpKind.Register when _currentInstruction.Op1Kind == OpKind.Immediate16:
                    Registers.SetValue(_currentInstruction.Op0Register, Registers.GetValue(_currentInstruction.Op0Register) + _currentInstruction.Immediate16);
                    break;
            }
        }

        /// <summary>
        ///     Push Op Code
        /// </summary>
        private void Op_Push()
        {
            switch (_currentInstruction.Op0Kind)
            {
                //PUSH r16
                case OpKind.Register:
                    Registers.SP -= 2;
                    Memory.PushWord(Registers.SP, (ushort)Registers.GetValue(_currentInstruction.Op0Register));
                    break;

                //PUSH imm8
                case OpKind.Immediate8:
                    Registers.SP -= 1;
                    Memory.PushByte(Registers.SP, _currentInstruction.Immediate8);
                    break;

                //PUSH imm16
                case OpKind.Immediate16:
                    Registers.SP -= 2;
                    Memory.PushWord(Registers.SP, _currentInstruction.Immediate16);
                    
                    break;
            }
        }

        /// <summary>
        ///     MOV Op Code
        /// </summary>
        private void Op_Mov()
        {
            switch (_currentInstruction.Op0Kind)
            {
                //MOV r16,imm16
                case OpKind.Register when _currentInstruction.Op1Kind == OpKind.Immediate16:
                {
                    //Check for a possible relocation
                    int destinationValue;

                    if (_currentInstruction.Immediate16 == ushort.MaxValue)
                    {
                        var relocationRecord = Memory._segments[Registers.CS].RelocationRecords
                            .FirstOrDefault(x => x.Offset == Registers.IP + 1);

                        //If we found a relocation record, set the new value, otherwise, it must have been a literal max
                        destinationValue = relocationRecord?.TargetTypeValueTuple.Item2 ?? ushort.MaxValue;
                    }
                    else
                    {
                        destinationValue = _currentInstruction.Immediate16;
                    }

                    Registers.SetValue(_currentInstruction.Op0Register, destinationValue);
                    return;
                }

                //MOV r16, r16
                case OpKind.Register when _currentInstruction.Op1Kind == OpKind.Register:
                {
                    Registers.SetValue(_currentInstruction.Op0Register,
                        Registers.GetValue(_currentInstruction.Op1Register));
                    return;
                }

                //MOV r/m16,imm16
                /*
                 * The instruction in the question only uses a single constant offset so no effective address with registers.
                 * As such, it's DS unless overridden by a prefix.
                 */
                case OpKind.Memory when _currentInstruction.Op1Kind == OpKind.Immediate16:
                {
                    Memory.SetWord(Registers.DS, (int) _currentInstruction.MemoryDisplacement, _currentInstruction.Immediate16);
                    break;
                } 
                
                
                case OpKind.Memory when _currentInstruction.Op1Kind == OpKind.Register:
                {
                    switch (_currentInstruction.MemorySize)
                    {
                        //MOV moffs16*,AX
                        case MemorySize.UInt16:
                            Memory.SetWord(Registers.DS, (int) _currentInstruction.MemoryDisplacement,
                                (ushort) Registers.GetValue(_currentInstruction.Op1Register));
                            break;
                        //MOV moffs8*,AL
                        case MemorySize.UInt8:
                            Memory.SetByte(Registers.DS, (int) _currentInstruction.MemoryDisplacement,
                                (byte) Registers.GetValue(_currentInstruction.Op1Register));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("Unsupported Memory type for MOV operation");
                    }

                    break;
                }
            }
        }

        private void Op_Call()
        {
            switch (_currentInstruction.Op0Kind)
            {
                case OpKind.FarBranch16 when _currentInstruction.Immediate16 == ushort.MaxValue:
                {
                    //Check for a possible relocation
                    int destinationValue;

                    if (_currentInstruction.Immediate16 == ushort.MaxValue)
                    {
                        var relocationRecord = Memory._segments[Registers.CS].RelocationRecords
                            .FirstOrDefault(x => x.Offset == Registers.IP + 1);

                        if (relocationRecord == null)
                        {
                            destinationValue = ushort.MaxValue;
                        }
                        else
                        {
                            _invokeExternalFunctionDelegate(relocationRecord.TargetTypeValueTuple.Item2,
                                relocationRecord.TargetTypeValueTuple.Item3);
                            return;
                        }
                    }
                    else
                    {
                        destinationValue = _currentInstruction.Immediate16;
                    }

                    //TODO -- Perform actual call

                    break;
                }
            }

        }
    }
}