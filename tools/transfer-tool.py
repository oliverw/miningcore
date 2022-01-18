from web3 import Web3
from getpass import getpass
import argparse

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('-s', '--source-addr', dest='sourceAddr', help='(required) source wallet address', type=str, required=True)
    parser.add_argument('-d', '--dest-addr', dest='destAddr', help='(required) destination wallet address', type=str, required=True)
    parser.add_argument('-a', '--amount', dest='amount', help='(required) amount in wei to transfer', type=int, required=True)
    parser.add_argument('-g', '--gas-price', dest='gasPrice', help='(optional) gas price in wei for transaction (default: current average)', type=int, default=-1, required=False)
    parser.add_argument('-n', '--nonce', dest='nonce', help='(optional) nonce for transaction. Use to overwrite/cancel existing transactions (next nonce in sequence for sourceAddr)', type=int, default=-1, required=False)
    parser.add_argument('--host', dest='host', help='URL for ethereum node (default: http://localhost:8545)', type=str, default='http://localhost:8545', required=False)

    args = parser.parse_args()

    pKey = getpass("Enter private key for source wallet:")

    if not Web3.isAddress(args.sourceAddr):
        print(f'Destination address {args.sourceAddr} is invalid')
        exit(1)

    if not Web3.isAddress(args.destAddr):
        print(f'Destination address {args.destAddr} is invalid')
        exit(1)

    web3 = Web3(Web3.HTTPProvider(args.host))
    if not web3.isConnected():
        print(f'Failed to connect to ethereum host: {args.host}')
        exit(1)

    if args.nonce == -1:
        args.nonce = web3.eth.get_transaction_count(args.sourceAddr)

    if args.gasPrice == -1:
        args.gasPrice = web3.eth.gas_price

    chainId = web3.eth.chain_id

    transaction = {
        'to': args.destAddr,
        'value': args.amount,
        'gas': 21000,
        'gasPrice': args.gasPrice,
        'nonce': args.nonce,
        'chainId': chainId
    }

    confirmationInput = input(f'Sending {args.amount / 1000000000000000000} ETH from {args.sourceAddr} to {args.destAddr} on chain {chainId}. Please confirm (Only "yes" will be accepted):')

    if confirmationInput != "yes":
        print('Did not recieve "yes" - cancelling transaction')
        exit(1)
    else:
        print('Confirmed. Sending...')

    signed = web3.eth.account.sign_transaction(transaction, pKey)
    txhash = web3.eth.send_raw_transaction(signed.rawTransaction)
    print(f'Transaction sent: {txhash.hex()}')

if __name__ == '__main__':
    main()